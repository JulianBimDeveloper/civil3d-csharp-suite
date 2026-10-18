using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace XTS_Evolution_PB
{
    /// <summary>
    /// HTTP bridge embebido en Civil 3D que expone endpoints para el MCP server de Claude.
    /// Escucha en http://localhost:8091/ en un hilo de fondo.
    /// Comandos AutoCAD se ejecutan via SendStringToExecute (thread-safe, hilo principal).
    /// </summary>
    public static class McpBridge
    {
        private const int Port = 8091;
        private static HttpListener _listener;
        private static Thread _thread;
        private static volatile bool _running;

        private static readonly JsonSerializerOptions _json = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        // ---------------------------------------------------------------------------
        // Ciclo de vida
        // ---------------------------------------------------------------------------

        public static void Start()
        {
            if (_running) return;
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{Port}/");
                _listener.Start();
                _running = true;
                _thread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "XTS-MCP-Bridge",
                };
                _thread.Start();
                Log($"[XTS MCP] Bridge activo → http://localhost:{Port}/");
            }
            catch (Exception ex)
            {
                Log($"[XTS MCP] Error al iniciar bridge: {ex.Message}");
            }
        }

        public static void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
        }

        private static void Log(string msg)
        {
            try
            {
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n{msg}\n");
            }
            catch { }
        }

        // ---------------------------------------------------------------------------
        // Loop de escucha
        // ---------------------------------------------------------------------------

        private static void ListenLoop()
        {
            while (_running)
            {
                try
                {
                    var ctx = _listener.GetContext();
                    _ = Task.Run(() => HandleRequestSafe(ctx));
                }
                catch when (!_running) { break; }
                catch { }
            }
        }

        private static async Task HandleRequestSafe(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            res.ContentType = "application/json; charset=utf-8";
            res.Headers.Add("Access-Control-Allow-Origin", "*");

            try
            {
                // OPTIONS preflight
                if (req.HttpMethod == "OPTIONS")
                {
                    res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                    res.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
                    res.StatusCode = 204;
                    res.Close();
                    return;
                }

                object result = (req.HttpMethod, req.Url.AbsolutePath.TrimEnd('/')) switch
                {
                    ("GET", "/status")   => GetStatus(),
                    ("GET", "/selection") => GetSelection(),
                    ("GET", "/dwg-info") => GetDwgInfo(),
                    ("POST", "/execute") => await ExecuteCommand(req),
                    ("POST", "/zoom")    => await ZoomToHandle(req),
                    _                    => new { error = $"Ruta no encontrada: {req.Url.AbsolutePath}" },
                };

                await WriteJson(res, result, 200);
            }
            catch (Exception ex)
            {
                await WriteJson(res, new { error = ex.Message }, 500);
            }
        }

        private static async Task WriteJson(HttpListenerResponse res, object obj, int statusCode)
        {
            var json = JsonSerializer.Serialize(obj, _json);
            var bytes = Encoding.UTF8.GetBytes(json);
            res.StatusCode = statusCode;
            res.ContentLength64 = bytes.Length;
            try
            {
                await res.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            }
            finally
            {
                res.OutputStream.Close();
            }
        }

        // ---------------------------------------------------------------------------
        // Handlers
        // ---------------------------------------------------------------------------

        private static object GetStatus()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            return new
            {
                ok = true,
                version = App.Version,
                bridge_port = Port,
                active_dwg = doc?.Name ?? "ninguno",
                civil3d_running = doc != null,
                timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            };
        }

        private static object GetDwgInfo()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return new { error = "No hay DWG activo en Civil 3D." };

            try
            {
                int objectCount = 0;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(
                        bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    foreach (ObjectId _ in btr) objectCount++;
                    tr.Commit();
                }
                return new
                {
                    name = Path.GetFileName(doc.Name),
                    full_path = doc.Name,
                    object_count_modelspace = objectCount,
                };
            }
            catch (Exception ex)
            {
                return new { error = ex.Message };
            }
        }

        private static object GetSelection()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return new { error = "No hay DWG activo." };

            try
            {
                var ss = doc.Editor.SelectImplied();
                if (ss.Status != PromptStatus.OK || ss.Value.Count == 0)
                    return new { count = 0, handles = Array.Empty<string>() };

                var handles = new List<string>();
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (SelectedObject so in ss.Value)
                    {
                        if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is Entity ent)
                            handles.Add(ent.Handle.ToString());
                    }
                    tr.Commit();
                }
                return new { count = handles.Count, handles };
            }
            catch (Exception ex)
            {
                return new { error = ex.Message };
            }
        }

        private static readonly HashSet<string> AllowedCommands = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "XTS_MASTER_SYNC", "XTS_HealthCheck", "XTS_HealthReset", "XTS_StampHandles",
            "XTS_SYNC_IFC", "XTS_ExportObjects", "XTS_ImportObjects", "XTS_MirrorProjectInfo",
            "XTS_ExportInfoProyecto", "XTS_ImportInfoProyecto", "XTS_AuditPsets",
            "XTS_SelectDuplicates", "XTS_SelectClean", "XTS_CopyAllParams", "XTS_INFO",
        };

        private static async Task<object> ExecuteCommand(HttpListenerRequest req)
        {
            var body = await ReadBody(req);
            if (!body.TryGetProperty("command", out var cmdEl))
                return new { error = "Falta campo 'command' en el body JSON." };

            var command = cmdEl.GetString()?.Trim();
            if (string.IsNullOrEmpty(command))
                return new { error = "Campo 'command' vacío." };

            if (!AllowedCommands.Contains(command))
                return new { error = $"Comando no permitido: '{command}'." };

            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return new { error = "No hay DWG activo en Civil 3D." };

            // SendStringToExecute es thread-safe: encola en el dispatcher de AutoCAD
            doc.SendStringToExecute($"{command} ", true, false, true);

            return new
            {
                ok = true,
                command,
                status = "scheduled",
                message = $"Comando '{command}' encolado en Civil 3D. Ejecutará en el próximo ciclo de eventos.",
            };
        }

        private static async Task<object> ZoomToHandle(HttpListenerRequest req)
        {
            var body = await ReadBody(req);
            if (!body.TryGetProperty("handle", out var hEl))
                return new { error = "Falta campo 'handle' en el body JSON." };

            var handle = hEl.GetString()?.Trim();
            if (string.IsNullOrEmpty(handle))
                return new { error = "Campo 'handle' vacío." };

            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return new { error = "No hay DWG activo." };

            // XTS_SelectByHandle ya admite el handle como argumento inline
            doc.SendStringToExecute($"XTS_SelectByHandle\n{handle}\n", true, false, true);

            return new { ok = true, handle, status = "scheduled" };
        }

        // ---------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------

        private static async Task<JsonElement> ReadBody(HttpListenerRequest req)
        {
            using var sr = new StreamReader(req.InputStream, Encoding.UTF8);
            var raw = await sr.ReadToEndAsync();
            return JsonSerializer.Deserialize<JsonElement>(raw);
        }
    }
}
