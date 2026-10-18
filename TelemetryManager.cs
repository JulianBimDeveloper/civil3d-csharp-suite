using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices.Core;

namespace XTS_Evolution_PB
{
    public static class TelemetryManager
    {
        private static readonly string BaseUrl = "http://127.0.0.1:8090/api/collections/xts_telemetry/records";
        private static readonly HttpClient _client = new HttpClient();

        public static async Task LogCommandAsync(string hwId, string command, string phase, string fileName, long durationMs, string status, string errorMsg = "")
        {
            try
            {
                var payload = new
                {
                    device_id = hwId,
                    command = command,
                    phase = phase,
                    file_name = fileName,
                    duration_ms = durationMs,
                    status = status,
                    error_msg = errorMsg,
                    app_version = "2.0.0-ENT"
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var res = await _client.PostAsync(BaseUrl, content);
                
                if (!res.IsSuccessStatusCode)
                {
                    var ed = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument?.Editor;
                    var body = await res.Content.ReadAsStringAsync();
                    ed?.WriteMessage($"\n⚠️ [XTS TELEMETRY]: Error al enviar log: {res.StatusCode} - {body}");
                }
            }
            catch (Exception ex)
            {
                var ed = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage($"\n❌ [XTS TELEMETRY ERROR]: {ex.Message}");
            }
        }
    }
}
