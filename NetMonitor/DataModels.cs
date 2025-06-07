using System;
using System.Collections.Generic;

namespace RDPLoginMonitor
{
    /// <summary>
    /// Уровни важности сообщений в логе
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// Обычная информация - черный цвет
        /// </summary>
        Info,

        /// <summary>
        /// Предупреждение - оранжевый цвет
        /// </summary>
        Warning,

        /// <summary>
        /// Ошибка - красный цвет
        /// </summary>
        Error,

        /// <summary>
        /// Успешная операция - зеленый цвет
        /// </summary>
        Success,

        /// <summary>
        /// Сетевая активность - синий цвет
        /// </summary>
        Network,

        /// <summary>
        /// Событие безопасности - фиолетовый цвет
        /// </summary>
        Security,

        /// <summary>
        /// Отладочная информация - серый цвет
        /// </summary>
        Debug
    }

    /// <summary>
    /// Модель попытки входа RDP
    /// </summary>
    public class RDPFailedLogin
    {
        /// <summary>
        /// Время события
        /// </summary>
        public DateTime TimeStamp { get; set; }

        /// <summary>
        /// Имя пользователя
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// IP адрес источника
        /// </summary>
        public string SourceIP { get; set; }

        /// <summary>
        /// Имя компьютера
        /// </summary>
        public string Computer { get; set; }

        /// <summary>
        /// ID события из журнала Windows
        /// </summary>
        public int EventId { get; set; }

        /// <summary>
        /// Описание события
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Статус (Успешный/Неудачный/и т.д.)
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Тип события (Успешный вход/Неудачный вход/и т.д.)
        /// </summary>
        public string EventType { get; set; }

        /// <summary>
        /// Тип логона (2=интерактивный, 3=сетевой, 10=RDP, и т.д.)
        /// </summary>
        public string LogonType { get; set; }
    }

    /// <summary>
    /// Модель сетевого устройства
    /// </summary>
    public class NetworkDevice
    {
        /// <summary>
        /// IP адрес устройства
        /// </summary>
        public string IPAddress { get; set; }

        /// <summary>
        /// MAC адрес устройства
        /// </summary>
        public string MACAddress { get; set; }

        /// <summary>
        /// Имя хоста
        /// </summary>
        public string Hostname { get; set; }

        /// <summary>
        /// Производитель (определяется по MAC адресу)
        /// </summary>
        public string Vendor { get; set; }

        /// <summary>
        /// Тип устройства (iPhone, Android, Компьютер, и т.д.)
        /// </summary>
        public string DeviceType { get; set; }

        /// <summary>
        /// Операционная система
        /// </summary>
        public string OperatingSystem { get; set; }

        /// <summary>
        /// Текущий статус (Активен/Недоступен/Ошибка)
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Время первого обнаружения
        /// </summary>
        public DateTime FirstSeen { get; set; }

        /// <summary>
        /// Время последней активности
        /// </summary>
        public DateTime LastSeen { get; set; }

        /// <summary>
        /// Флаг нового устройства
        /// </summary>
        public bool IsNew { get; set; }

        /// <summary>
        /// Список открытых портов
        /// </summary>
        public List<int> OpenPorts { get; set; } = new List<int>();

        /// <summary>
        /// Описание устройства
        /// </summary>
        public string Description { get; set; }
    }
}