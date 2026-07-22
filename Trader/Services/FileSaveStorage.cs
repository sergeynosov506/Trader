using System;
using System.Collections.Generic;
using System.IO;

namespace EconomicGame.Services
{
    /// <summary>
    /// Server-host save storage: JSON files in %AppData%/Trader/Saves.
    /// </summary>
    public class FileSaveStorage : ISaveStorage
    {
        private readonly string _savePath;

        public FileSaveStorage()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _savePath = Path.Combine(appData, "Trader", "Saves");
            Directory.CreateDirectory(_savePath);
        }

        public List<(string Name, string Json)> ReadAll()
        {
            var result = new List<(string, string)>();
            if (!Directory.Exists(_savePath)) return result;

            foreach (var file in Directory.GetFiles(_savePath, "*.json"))
            {
                try
                {
                    result.Add((Path.GetFileNameWithoutExtension(file), File.ReadAllText(file)));
                }
                catch { /* Skip unreadable files */ }
            }
            return result;
        }

        private string PathFor(string name) => Path.Combine(_savePath, name + ".json");

        public string? Read(string name) => File.Exists(PathFor(name)) ? File.ReadAllText(PathFor(name)) : null;

        public void Write(string name, string json) => File.WriteAllText(PathFor(name), json);

        public bool Exists(string name) => File.Exists(PathFor(name));

        public void Delete(string name)
        {
            if (File.Exists(PathFor(name))) File.Delete(PathFor(name));
        }
    }
}
