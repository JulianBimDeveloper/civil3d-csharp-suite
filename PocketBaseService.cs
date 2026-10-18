using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace XTS_Evolution_PB
{
    public class IfcMasterConfig
    {
        public string config_json { get; set; }
        public string mapping_json { get; set; }
        public string property_mapping_json { get; set; }
    }

    public class PocketBaseService
    {
        // Credenciales leídas del entorno. Configurar XTS_PB_URL / XTS_PB_EMAIL / XTS_PB_PASSWORD
        // como variables de sistema en Windows antes de NETLOAD.
        private static string PB_URL => Environment.GetEnvironmentVariable("XTS_PB_URL") ?? "http://127.0.0.1:8090";
        private static string PB_EMAIL => Environment.GetEnvironmentVariable("XTS_PB_EMAIL") ?? "extasisjdm@gmail.com";
        private static string PB_PASSWORD => Environment.GetEnvironmentVariable("XTS_PB_PASSWORD") ?? "";
        
        private static string _token = null;

        // Timeout parametrizable desde .env (PB_TIMEOUT_SECONDS), default 30s
        private static readonly int _pbTimeoutSeconds =
            int.TryParse(Environment.GetEnvironmentVariable("PB_TIMEOUT_SECONDS"), out int t) ? t : 30;

        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(_pbTimeoutSeconds)
        };

        private static void LogErr(string trigger, Exception ex)
        {
            try {
                var ed = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage($"\n❌ [PB Error {trigger}]: {ex.Message}");
            } catch { } // Ignorar si no hay UI de AutoCAD
        }

        private static async Task<string> GetToken()
        {
            if (_token != null) return _token;
            try
            {
                var authData = new { identity = PB_EMAIL, password = PB_PASSWORD };
                var response = await _httpClient.PostAsync($"{PB_URL}/api/admins/auth-with-password",
                    new StringContent(JsonSerializer.Serialize(authData), Encoding.UTF8, "application/json"));
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(content);
                    _token = doc.RootElement.GetProperty("token").GetString();
                    return _token;
                }
            } catch(Exception ex) { LogErr("GetToken", ex); }
            return null;
        }

        public static async Task<bool> UpsertRecord(string collection, object data, string filter)
        {
            var ed = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument.Editor;
            try
            {
                string token = await GetToken();
                if (token == null) { ed.WriteMessage("\n❌ ERROR: Token de PocketBase no disponible."); return false; }

                // 1. Verificar existencia
                string json = await GetRecordsAsync(collection, filter);
                string recordId = null;

                if (!json.StartsWith("ERROR:")) {
                    using var doc = JsonDocument.Parse(json);
                    var items = doc.RootElement.GetProperty("items");
                    if (items.GetArrayLength() > 0) recordId = items[0].GetProperty("id").GetString();
                }

                // 2. Preparar Request
                string url = $"{PB_URL}/api/collections/{collection}/records";
                HttpMethod method = HttpMethod.Post;

                if (!string.IsNullOrEmpty(recordId)) {
                    url += $"/{recordId}";
                    method = new HttpMethod("PATCH");
                }

                var request = new HttpRequestMessage(method, url);
                request.Headers.Add("Authorization", token);
                request.Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode) return true;

                string errBody = await response.Content.ReadAsStringAsync();
                ed.WriteMessage($"\n❌ PB Error ({response.StatusCode}): {errBody}");
                return false;
            }
            catch (Exception ex) { 
                ed.WriteMessage($"\n❌ Upsert Exception: {ex.Message}");
                return false; 
            }
        }

        public static async Task<string> GetRecordsAsync(string collection, string filter, int perPage = 50, int page = 1)
        {
            try
            {
                string token = await GetToken();
                if (token == null) return "ERROR: Auth failed";

                string url = $"{PB_URL}/api/collections/{collection}/records?perPage={perPage}&page={page}";
                if (!string.IsNullOrEmpty(filter)) url += $"&filter=({Uri.EscapeDataString(filter)})";
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", token);
                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode) return await response.Content.ReadAsStringAsync();
                return $"ERROR: {response.StatusCode}";
            } catch (Exception ex) { LogErr("GetRecords", ex); return $"ERROR: {ex.Message}"; }
        }

        public static async Task<List<JsonElement>> GetAllRecordsAsync(string collection, string filter = "")
        {
            var allItems = new List<JsonElement>();
            int page = 1;
            int perPage = 500; // Max admin standard pocketbase
            
            try {
                while (true) {
                    string json = await GetRecordsAsync(collection, filter, perPage, page);
                    if (json.StartsWith("ERROR:")) break;

                    using var doc = JsonDocument.Parse(json);
                    var items = doc.RootElement.GetProperty("items").Clone();
                    int totalItems = doc.RootElement.GetProperty("totalItems").GetInt32();

                    foreach (var item in items.EnumerateArray()) {
                        allItems.Add(item);
                    }

                    if (allItems.Count >= totalItems || items.GetArrayLength() < perPage) break;
                    page++;
                }
            } catch (Exception ex) { LogErr("GetAllRecords", ex); }
            return allItems;
        }

        public static async Task<IfcMasterConfig> GetMasterConfigAsync()
        {
            try
            {
                // Buscamos el registro único en la tabla xts_ifc_masters
                string json = await GetRecordsAsync("xts_ifc_masters", "name='ADIF_Master'");
                if (json.StartsWith("ERROR:") || string.IsNullOrEmpty(json)) return null;

                using var jd = JsonDocument.Parse(json);
                var items = jd.RootElement.GetProperty("items");
                if (items.GetArrayLength() == 0) return null;

                var first = items[0];
                return new IfcMasterConfig
                {
                    config_json = first.TryGetProperty("config_json", out var c) ? c.GetString() : "",
                    mapping_json = first.TryGetProperty("mapping_json", out var m) ? m.GetString() : "",
                    property_mapping_json = first.TryGetProperty("property_mapping_json", out var p) ? p.GetString() : ""
                };
            }
            catch (Exception ex) { LogErr("GetMasterConfig", ex); return null; }
        }

        public static async Task<string> GetOrCreateRecord(string collection, string nameField, string nameValue)
        {
            try {
                // 1. Buscar si existe
                string filter = $"{nameField} = \"{nameValue}\"";
                string json = await GetRecordsAsync(collection, filter);
                
                if (!json.StartsWith("ERROR:")) {
                    using var doc = JsonDocument.Parse(json);
                    var items = doc.RootElement.GetProperty("items");
                    if (items.GetArrayLength() > 0) return items[0].GetProperty("id").GetString();
                }

                // 2. Si no existe, crear
                string token = await GetToken();
                var data = new Dictionary<string, string> { { nameField, nameValue } };
                var request = new HttpRequestMessage(HttpMethod.Post, $"{PB_URL}/api/collections/{collection}/records");
                request.Headers.Add("Authorization", token);
                request.Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode) {
                    var content = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(content);
                    return doc.RootElement.GetProperty("id").GetString();
                }
            } catch (Exception ex) { LogErr("GetOrCreateRecord", ex); }
            return null;
        }

        public static async Task PurgeCollectionAsync(string collection, string filter)
        {
            var ed = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument.Editor;
            try
            {
                string json = await GetRecordsAsync(collection, filter);
                if (json.StartsWith("ERROR:")) return;

                using var doc = JsonDocument.Parse(json);
                var items = doc.RootElement.GetProperty("items");
                
                string token = await GetToken();
                foreach (var item in items.EnumerateArray())
                {
                    string id = item.GetProperty("id").GetString();
                    var request = new HttpRequestMessage(HttpMethod.Delete, $"{PB_URL}/api/collections/{collection}/records/{id}");
                    request.Headers.Add("Authorization", token);
                    await _httpClient.SendAsync(request);
                }
            } catch (Exception ex) { LogErr("PurgeCollection", ex); }
        }
    }
}
