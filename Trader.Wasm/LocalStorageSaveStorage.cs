using System;
using System.Collections.Generic;
using Microsoft.JSInterop;

namespace EconomicGame.Services
{
    /// <summary>
    /// WebAssembly save storage: browser localStorage via SYNCHRONOUS JS interop
    /// (IJSInProcessRuntime is always available in Blazor WASM). Saves survive
    /// page reloads and browser restarts on the same device.
    /// Keys are prefixed to avoid clashing with anything else in localStorage.
    /// </summary>
    public class LocalStorageSaveStorage : ISaveStorage
    {
        private const string Prefix = "trader-save:";
        private readonly IJSInProcessRuntime _js;

        public LocalStorageSaveStorage(IJSRuntime js)
        {
            _js = (IJSInProcessRuntime)js;
        }

        public List<(string Name, string Json)> ReadAll()
        {
            var result = new List<(string, string)>();
            // traderStorage.keys is defined in wwwroot/index.html
            var keys = _js.Invoke<string[]>("traderStorage.keys");
            foreach (var key in keys)
            {
                var json = _js.Invoke<string?>("localStorage.getItem", key);
                if (json != null)
                {
                    result.Add((key.Substring(Prefix.Length), json));
                }
            }
            return result;
        }

        public string? Read(string name) =>
            _js.Invoke<string?>("localStorage.getItem", Prefix + name);

        public void Write(string name, string json) =>
            _js.InvokeVoid("localStorage.setItem", Prefix + name, json);

        public bool Exists(string name) => Read(name) != null;

        public void Delete(string name) =>
            _js.InvokeVoid("localStorage.removeItem", Prefix + name);
    }
}
