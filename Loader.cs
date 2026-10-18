using System;
using System.IO;
using System.Reflection;
using System.Linq;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;

[assembly: ExtensionApplication(typeof(XTS_Evolution_PB.Loader))]

namespace XTS_Evolution_PB
{
    public class Loader : IExtensionApplication
    {
        private static Assembly _latestAssembly = null;

        private static string RealDllPath
        {
            get
            {
                // 1. Intentar leer XTS_ROOT de env vars
                var xtsRoot = Environment.GetEnvironmentVariable("XTS_ROOT");

                // 2. Si no está, buscar .env en directorios padres y extraer XTS_ROOT
                if (string.IsNullOrEmpty(xtsRoot))
                {
                    xtsRoot = FindXtsRootFromEnvFile();
                }

                // 3. Fallback final (ruta por defecto de AsuntoBIM)
                if (string.IsNullOrEmpty(xtsRoot))
                    xtsRoot = @"C:\Users\JulianBimDeveloper\OneDrive - NUPD COMPUTER S.L\Documentos\JulianBimDeveloper\XTS MODELS SB";

                return Path.Combine(
                    xtsRoot.TrimEnd('\\'),
                    @"ADDINS\civil3d\code\XTS_C3D_Suite\bin\Debug\net8.0-windows\XTS_Evolution_PB.dll"
                );
            }
        }

        /// <summary>
        /// Busca el archivo .env en directorios padres y extrae XTS_ROOT.
        /// </summary>
        private static string FindXtsRootFromEnvFile()
        {
            var searchDir = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(searchDir))
            {
                var envPath = Path.Combine(searchDir, ".env");
                if (File.Exists(envPath))
                {
                    try
                    {
                        var lines = File.ReadAllLines(envPath);
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("XTS_ROOT="))
                            {
                                var value = line.Substring("XTS_ROOT=".Length).Trim();
                                // Remover comillas si existen
                                value = value.Trim('"').Trim('\'');
                                // Reemplazar separadores forward slash
                                value = value.Replace("/", "\\");
                                if (!string.IsNullOrEmpty(value))
                                    return value;
                            }
                        }
                    }
                    catch { /* Ignorar si hay errores leyendo */ }
                }

                var parent = Path.GetDirectoryName(searchDir);
                if (parent == searchDir) break; // Alcanzamos raíz del disco
                searchDir = parent;
            }

            return null;
        }
        private static Loader _instance;

        public void Initialize()
        {
            _instance = this;
            var doc = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
            if (doc != null) doc.Editor.WriteMessage("\n✅ [XTS Loader] Inicializado.");

            if (ComponentManager.Ribbon == null)
                Autodesk.AutoCAD.ApplicationServices.Core.Application.Idle += OnIdle;
            else
                CreateLoaderRibbon();

            // Intento de carga inicial al abrir
            if (File.Exists(RealDllPath)) ReloadPlugin();
        }

        private void OnIdle(object sender, EventArgs e)
        {
            if (ComponentManager.Ribbon != null)
            {
                Autodesk.AutoCAD.ApplicationServices.Core.Application.Idle -= OnIdle;
                CreateLoaderRibbon();
                ReloadPlugin();
            }
        }

        public void Terminate() { }

        // --- COMANDOS ESTRUCTURALES (Solo en el Cargador) ---
        [CommandMethod("XTS_RELOAD")]
        public static void XTS_RELOAD_CMD() => ReloadPlugin();

        [CommandMethod("XTS_RUN")]
        public static void XTS_RUN_CMD() => RunCommandProxy();

        public static void ReloadPlugin()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            try
            {
                ed.WriteMessage($"\n🔄 [XTS] Recargando lógica: {Path.GetFileName(RealDllPath)}...");

                if (!File.Exists(RealDllPath))
                {
                    ed.WriteMessage($"\n❌ [XTS] No se encuentra el archivo en {RealDllPath}");
                    return;
                }

                byte[] buffer = File.ReadAllBytes(RealDllPath);
                _latestAssembly = Assembly.Load(buffer);

                if (_latestAssembly != null)
                {
                    Type appType = _latestAssembly.GetType("XTS_Evolution_PB.App");
                    if (appType != null)
                    {
                        var instance = Activator.CreateInstance(appType);
                        var initMethod = appType.GetMethod("Initialize");
                        if (initMethod != null)
                        {
                             initMethod.Invoke(instance, null);
                             _instance?.CreateLoaderRibbon(); // RECUPERAR EL BOTÓN DE ACTUALIZAR
                             ed.WriteMessage($"\n🚀 [XTS] Lógica sincronizada (v2.2.1 - {DateTime.Now:HH:mm})");
                         }
                    }
                }
            }
            catch (System.Exception ex) 
            { 
                ed.WriteMessage($"\n❌ [XTS] Fallo de recarga: {ex.Message}");
            }
        }

        private void CreateLoaderRibbon()
        {
            var ribbonControl = ComponentManager.Ribbon;
            if (ribbonControl == null) return;

            var tab = ribbonControl.Tabs.FirstOrDefault(t => t.Id == "XTS_SUITE_TAB");
            if (tab == null)
            {
                tab = new RibbonTab { Title = "XTS Suite", Id = "XTS_SUITE_TAB" };
                ribbonControl.Tabs.Add(tab);
            }

            var sourceAdmin = new RibbonPanelSource { Title = "Admin" };
            var panelAdmin = new RibbonPanel { Source = sourceAdmin };
            
            var existingAdmin = tab.Panels.FirstOrDefault(p => p.Source.Title == "Admin");
            if (existingAdmin != null) tab.Panels.Remove(existingAdmin);
            
            tab.Panels.Insert(0, panelAdmin);

            var btnReload = new RibbonButton
            {
                Text = "Sync\nAdd-in",
                ShowText = true, 
                ShowImage = true, 
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                CommandParameter = "XTS_RELOAD ",
                CommandHandler = new RibbonCommandHandlerLoader()
            };

            btnReload.LargeImage = GetLoaderIcon();
            btnReload.Image = GetLoaderIcon();

            sourceAdmin.Items.Add(btnReload);
            tab.IsActive = true;
        }

        private System.Windows.Media.ImageSource GetLoaderIcon()
        {
            var drawingGroup = new System.Windows.Media.DrawingGroup();
            var color = System.Windows.Media.Color.FromRgb(0, 255, 255); 
            
            var glowBrush = new System.Windows.Media.SolidColorBrush(color) { Opacity = 0.4 };
            drawingGroup.Children.Add(new System.Windows.Media.GeometryDrawing(null, 
                new System.Windows.Media.Pen(glowBrush, 2.5), 
                new System.Windows.Media.EllipseGeometry(new System.Windows.Point(16, 16), 13, 13)));

            drawingGroup.Children.Add(new System.Windows.Media.GeometryDrawing(
                new System.Windows.Media.SolidColorBrush(color), 
                new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 0.5), 
                new System.Windows.Media.EllipseGeometry(new System.Windows.Point(16, 16), 11, 11)));

            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(32, 32, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            var drawingVisual = new System.Windows.Media.DrawingVisual();
            using (var dc = drawingVisual.RenderOpen()) { dc.DrawDrawing(drawingGroup); }
            bmp.Render(drawingVisual);
            return bmp;
        }

        public static void RunCommandProxy()
        {
            var ed = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument.Editor;
            var prompt = ed.GetString("\nComando interno (XTS_...): ");
            if (prompt.Status != Autodesk.AutoCAD.EditorInput.PromptStatus.OK) return;
            ExecuteInternal(prompt.StringResult);
        }

        public static void ExecuteInternal(string cmdName)
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
            var ed = doc?.Editor;
            
            if (_latestAssembly == null) {
                ed?.WriteMessage("\n❌ [XTS] ERROR: No hay ensamblado de lógica cargado.");
                return;
            }

            try
            {
                var cmdType = _latestAssembly.GetType("XTS_Evolution_PB.Commands");
                if (cmdType == null) {
                    ed?.WriteMessage("\n❌ [XTS] ERROR: No se encontró la clase 'XTS_Evolution_PB.Commands'.");
                    return;
                }

                // Intentamos encontrar el método por nombre exacto o por GlobalName del atributo
                var method = cmdType.GetMethod(cmdName);
                if (method == null)
                {
                    method = cmdType.GetMethods()
                        .FirstOrDefault(m => string.Equals(m.Name, cmdName, StringComparison.OrdinalIgnoreCase));
                }

                if (method != null)
                {
                    var instance = Activator.CreateInstance(cmdType);
                    method.Invoke(instance, null);
                }
                else
                {
                    ed?.WriteMessage($"\n❌ [XTS] ERROR: El comando '{cmdName}' no existe en la lógica.");
                }
            }
            catch (System.Exception ex) 
            { 
                ed?.WriteMessage($"\n❌ [XTS] ERROR CRÍTICO en {cmdName}: {ex.Message}");
                if (ex.InnerException != null) ed?.WriteMessage($"\n   - Detalle: {ex.InnerException.Message}");
            }
        }
    }

    public class RibbonCommandHandlerLoader : System.Windows.Input.ICommand
    {
        public event EventHandler CanExecuteChanged;
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter)
        {
            if (parameter is RibbonButton rb)
                Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument.SendStringToExecute((string)rb.CommandParameter, true, false, true);
        }
    }
}
