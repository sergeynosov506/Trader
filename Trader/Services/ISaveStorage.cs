using System.Collections.Generic;

namespace EconomicGame.Services
{
    /// <summary>
    /// Abstraction over "where save files live".
    /// Server host: real files in %AppData%/Trader/Saves (FileSaveStorage).
    /// WebAssembly host: browser localStorage via synchronous JS interop.
    /// Keys are logical save names WITHOUT extension.
    /// </summary>
    public interface ISaveStorage
    {
        /// <summary>All stored saves as (name, json) pairs. Corrupted entries may be skipped by the caller.</summary>
        List<(string Name, string Json)> ReadAll();

        /// <summary>Read one save's json, or null if it doesn't exist.</summary>
        string? Read(string name);

        void Write(string name, string json);

        bool Exists(string name);

        void Delete(string name);
    }
}
