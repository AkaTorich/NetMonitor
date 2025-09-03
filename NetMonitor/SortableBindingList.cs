// Добавь этот класс в отдельный файл SortableBindingList.cs или в конец MainForm.cs

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

/// <summary>
/// BindingList с поддержкой сортировки для DataGridView
/// </summary>
/// <typeparam name="T">Тип объектов в списке</typeparam>
public class SortableBindingList<T> : BindingList<T>
{
	private bool _isSorted;
	private PropertyDescriptor _sortProperty;
	private ListSortDirection _sortDirection;

	// Новое: управление автоматической пересортировкой при изменениях
	public bool AutoResortOnChange { get; set; } = false;

	public SortableBindingList() : base() { }

	public SortableBindingList(IList<T> list) : base(list) { }

	protected override bool SupportsSortingCore => true;

	protected override bool IsSortedCore => _isSorted;

	protected override PropertyDescriptor SortPropertyCore => _sortProperty;

	protected override ListSortDirection SortDirectionCore => _sortDirection;

	protected override void ApplySortCore(PropertyDescriptor property, ListSortDirection direction)
	{
		try
		{
			var items = this.Items as List<T>;
			if (items != null)
			{
				var query = items.AsQueryable();

				// Создаем параметр для лямбда-выражения
				var param = System.Linq.Expressions.Expression.Parameter(typeof(T), "x");

				// Получаем свойство для сортировки
				var sortProperty = System.Linq.Expressions.Expression.Property(param, property.Name);

				// Создаем лямбда-выражение
				var lambda = System.Linq.Expressions.Expression.Lambda(sortProperty, param);

				// Применяем сортировку
				string methodName = direction == ListSortDirection.Ascending ? "OrderBy" : "OrderByDescending";

				var resultExpression = System.Linq.Expressions.Expression.Call(
					typeof(Queryable),
					methodName,
					new Type[] { typeof(T), sortProperty.Type },
					query.Expression,
					lambda);

				var sortedQuery = query.Provider.CreateQuery<T>(resultExpression);
				var sortedList = sortedQuery.ToList();

				// Очищаем список и добавляем отсортированные элементы
				this.Items.Clear();
				foreach (var item in sortedList)
				{
					this.Items.Add(item);
				}

				_sortProperty = property;
				_sortDirection = direction;
				_isSorted = true;

				// Уведомляем об изменении списка
				this.OnListChanged(new ListChangedEventArgs(ListChangedType.Reset, -1));
			}
		}
		catch (Exception ex)
		{
			// В случае ошибки сортировки, просто игнорируем
			System.Diagnostics.Debug.WriteLine($"Ошибка сортировки: {ex.Message}");
		}
	}

	protected override void RemoveSortCore()
	{
		_isSorted = false;
		_sortProperty = null;
		_sortDirection = ListSortDirection.Ascending;
	}

	/// <summary>
	/// Принудительно пересортировать список по последнему свойству/направлению
	/// </summary>
	public void Resort()
	{
		if (_isSorted && _sortProperty != null)
		{
			ApplySortCore(_sortProperty, _sortDirection);
		}
	}

	/// <summary>
	/// Добавляет элемент с сохранением сортировки (если включено)
	/// </summary>
	public new void Add(T item)
	{
		base.Add(item);

		// Если список отсортирован, пересортировываем в зависимости от флага
		if (_isSorted && _sortProperty != null && AutoResortOnChange)
		{
			ApplySortCore(_sortProperty, _sortDirection);
		}
	}

	/// <summary>
	/// Вставляет элемент в начало с сохранением сортировки (если включено)
	/// </summary>
	public void Insert(T item)
	{
		this.Insert(0, item);

		// Если список отсортирован, пересортировываем в зависимости от флага
		if (_isSorted && _sortProperty != null && AutoResortOnChange)
		{
			ApplySortCore(_sortProperty, _sortDirection);
		}
	}
}

// ТАКЖЕ добавь этот улучшенный класс для IP-адресов:
public static class IPAddressHelper
{
	/// <summary>
	/// Конвертирует IP адрес в число для правильной сортировки
	/// </summary>
	public static uint ToUInt32(string ipAddress)
	{
		try
		{
			if (string.IsNullOrEmpty(ipAddress) || ipAddress == "Unknown" || ipAddress == "-")
				return 0;

			var parts = ipAddress.Split('.');
			if (parts.Length != 4)
				return 0;

			return (uint)(
				(uint.Parse(parts[0]) << 24) |
				(uint.Parse(parts[1]) << 16) |
				(uint.Parse(parts[2]) << 8) |
				uint.Parse(parts[3])
			);
		}
		catch
		{
			return 0; // Если не удается распарсить, возвращаем 0
		}
	}

	/// <summary>
	/// Сравнивает два IP адреса как числа
	/// </summary>
	public static int Compare(string ip1, string ip2)
	{
		var num1 = ToUInt32(ip1);
		var num2 = ToUInt32(ip2);
		return num1.CompareTo(num2);
	}
}