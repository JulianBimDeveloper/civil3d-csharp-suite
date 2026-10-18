using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices.Core;

namespace XTS_Evolution_PB
{
    public static class SecurityManager
    {
        private static readonly string BaseUrl = "http://127.0.0.1:8090/api/collections";
        private static readonly HttpClient _client = new HttpClient();

        public enum AccessStatus { Authorized, GracePeriod, Denied }

        public static string GetHardwareId()
        {
            try
            {
                using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("MachineGuid");
                        if (val != null) return val.ToString();
                    }
                }
            }
            catch { }
            return Environment.MachineName + "_" + Environment.UserName;
        }

        public static async Task<AccessStatus> CheckAccessAsync()
        {
            string hwId = GetHardwareId();
            try
            {
                // Usamos ConfigureAwait(false) para evitar deadlocks en AutoCAD
                var response = await _client.GetAsync($"{BaseUrl}/xts_devices/records?filter=(hardware_id='{hwId}')").ConfigureAwait(false);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        var items = doc.RootElement.GetProperty("items");
                        if (items.GetArrayLength() > 0)
                        {
                            var device = items[0];
                            string status = device.GetProperty("status").GetString();
                            DateTime firstSeen = device.GetProperty("first_seen").GetDateTime();

                            _ = UpdateLastSeen(device.GetProperty("id").GetString());

                            if (status == "approved") return AccessStatus.Authorized;
                            if (status == "blocked") return AccessStatus.Denied;
                            
                            if (DateTime.UtcNow.Subtract(firstSeen.ToUniversalTime()).TotalHours < 24)
                                return AccessStatus.GracePeriod;
                            
                            return AccessStatus.Denied;
                        }
                    }
                }

                // Si no existe, intentar registrar
                return await RegisterDevice(hwId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return AccessStatus.Authorized; 
            }
        }

        public static async Task<AccessStatus> RegisterDevice(string hwId)
        {
            try
            {
                var payload = new
                {
                    hardware_id = hwId,
                    pc_name = Environment.MachineName,
                    win_user = Environment.UserName,
                    status = "pending",
                    first_seen = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fffZ"),
                    last_seen = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fffZ")
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var res = await _client.PostAsync($"{BaseUrl}/xts_devices/records", content).ConfigureAwait(false);
                
                if (res.IsSuccessStatusCode)
                {
                    return AccessStatus.GracePeriod;
                }
                else
                {
                    string body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    throw new Exception($"PB Reject: {res.StatusCode} - {body}");
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public static async Task UpdateLastSeen(string recordId)
        {
            try
            {
                var payload = new { last_seen = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fffZ") };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                await _client.PatchAsync($"{BaseUrl}/xts_devices/records/{recordId}", content).ConfigureAwait(false);
            }
            catch { }
        }
    }
}
