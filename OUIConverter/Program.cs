using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

namespace OUIConverter
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== IEEE OUI to MAC.db Converter ===\n");

            string inputFile = "oui.txt";
            string outputFile = "MAC.db";

            if (args.Length >= 1)
                inputFile = args[0];
            if (args.Length >= 2)
                outputFile = args[1];

            if (!File.Exists(inputFile))
            {
                Console.WriteLine($"Ошибка: Файл {inputFile} не найден!");
                Console.WriteLine("\nИспользование:");
                Console.WriteLine("OUIConverter.exe [входной_файл] [выходной_файл]");
                Console.WriteLine("\nПо умолчанию:");
                Console.WriteLine("  Входной файл: oui.txt");
                Console.WriteLine("  Выходной файл: MAC.db");
                Console.ReadKey();
                return;
            }

            try
            {
                ConvertOUIToMACDB(inputFile, outputFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                Console.ReadKey();
            }
        }

        static void ConvertOUIToMACDB(string inputFile, string outputFile)
        {
            Console.WriteLine($"Читаем файл: {inputFile}");

            var lines = File.ReadAllLines(inputFile, Encoding.UTF8);
            var entries = new Dictionary<string, string>();

            // Регулярное выражение для поиска MAC префикса и организации
            // Формат: XX-XX-XX   (hex)		Organization Name
            var regex = new Regex(@"^([0-9A-Fa-f]{2}-[0-9A-Fa-f]{2}-[0-9A-Fa-f]{2})\s+\(hex\)\s+(.+)$");

            int processed = 0;
            int skipped = 0;

            Console.WriteLine($"Всего строк в файле: {lines.Length}");
            Console.WriteLine("Обработка...");

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    skipped++;
                    continue;
                }

                var match = regex.Match(line);
                if (match.Success)
                {
                    // Извлекаем MAC префикс и убираем дефисы
                    var macPrefix = match.Groups[1].Value.Replace("-", "").ToUpper();
                    var organization = match.Groups[2].Value.Trim();

                    // Добавляем в словарь (перезаписываем если дубликат)
                    entries[macPrefix] = organization;
                    processed++;

                    if (processed % 1000 == 0)
                    {
                        Console.Write($"\rОбработано: {processed} записей");
                    }
                }
                else
                {
                    // Пропускаем строки, которые не соответствуют формату
                    skipped++;
                }
            }

            Console.WriteLine($"\n\nНайдено MAC префиксов: {entries.Count}");
            Console.WriteLine($"Пропущено строк: {skipped}");

            // Сортируем по MAC префиксу
            var sortedEntries = entries.OrderBy(x => x.Key).ToList();

            // Записываем в выходной файл
            Console.WriteLine($"\nЗаписываем в файл: {outputFile}");

            using (var writer = new StreamWriter(outputFile, false, Encoding.UTF8))
            {
                foreach (var entry in sortedEntries)
                {
                    writer.WriteLine($"{entry.Key}\t{entry.Value}");
                }
            }

            Console.WriteLine($"Готово! Создан файл {outputFile} с {sortedEntries.Count} записями.");

            // Показываем примеры первых 10 записей
            Console.WriteLine("\nПримеры первых 10 записей:");
            for (int i = 0; i < Math.Min(10, sortedEntries.Count); i++)
            {
                Console.WriteLine($"{sortedEntries[i].Key}\t{sortedEntries[i].Value}");
            }

            // Проверяем наличие известных префиксов
            Console.WriteLine("\nПроверка известных префиксов:");
            CheckKnownPrefixes(entries);

            Console.WriteLine("\nНажмите любую клавишу для выхода...");
            Console.ReadKey();
        }

        static void CheckKnownPrefixes(Dictionary<string, string> entries)
        {
            var testPrefixes = new Dictionary<string, string>
            {
                { "FC253F", "Apple" },
                { "04D4C4", "ASUSTek" },
                { "C4EB42", "TP-Link" },
                { "000000", "Xerox" },
                { "08EA44", "Extreme Networks" }
            };

            foreach (var test in testPrefixes)
            {
                if (entries.ContainsKey(test.Key))
                {
                    Console.WriteLine($"✓ {test.Key} -> {entries[test.Key]}");
                }
                else
                {
                    Console.WriteLine($"✗ {test.Key} -> НЕ НАЙДЕН (ожидался {test.Value})");
                }
            }
        }
    }
}