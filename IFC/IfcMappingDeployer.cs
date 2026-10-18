using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace XTS_Evolution_PB.IFC
{
    public sealed record IfcMappingDeployResult(
        bool Success,
        string TargetFolder,
        IList<string> Copied,
        IList<string> BackedUp,
        string Error
    );

    public static class IfcMappingDeployer
    {
        // Mapeo: nombre del archivo destino → LogicalName del recurso embebido.
        private static readonly Dictionary<string, string> Resources = new()
        {
            ["IfcInfraConfiguration.json"]         = "XTS_Evolution_PB.IFC.Resources.IfcInfraConfiguration.json",
            ["IfcInfraExportMapping.json"]         = "XTS_Evolution_PB.IFC.Resources.IfcInfraExportMapping.json",
            ["IfcInfraExportPropertyMapping.json"] = "XTS_Evolution_PB.IFC.Resources.IfcInfraExportPropertyMapping.json",
        };

        public static IfcMappingDeployResult Deploy(string overrideFolder = null)
        {
            try
            {
                string folder = overrideFolder ?? GetTargetFolder();
                Directory.CreateDirectory(folder);

                var copied = new List<string>();
                var backedUp = new List<string>();
                var asm = Assembly.GetExecutingAssembly();

                foreach (var kv in Resources)
                {
                    string targetPath = Path.Combine(folder, kv.Key);

                    if (File.Exists(targetPath))
                    {
                        string bak = targetPath + ".bak";
                        File.Copy(targetPath, bak, overwrite: true);
                        backedUp.Add(Path.GetFileName(bak));
                    }

                    using var stream = asm.GetManifestResourceStream(kv.Value)
                        ?? throw new FileNotFoundException($"Recurso embebido no encontrado: {kv.Value}");
                    using var fs = File.Create(targetPath);
                    stream.CopyTo(fs);
                    copied.Add(kv.Key);
                }

                return new IfcMappingDeployResult(true, folder, copied, backedUp, null);
            }
            catch (Exception ex)
            {
                return new IfcMappingDeployResult(false, "", new List<string>(), new List<string>(), ex.Message);
            }
        }

        // Civil 3D 2025 carga estos JSON desde un path configurable en el cuadro de Export IFC.
        // Usamos el path típico de AppData. Override vía variable de entorno XTS_IFC_MAPPING_PATH.
        public static string GetTargetFolder()
        {
            var envOverride = Environment.GetEnvironmentVariable("XTS_IFC_MAPPING_PATH");
            if (!string.IsNullOrWhiteSpace(envOverride)) return envOverride;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string[] candidates =
            {
                Path.Combine(appData, "Autodesk", "C3D 2025", "enu", "Data", "IFC"),
                Path.Combine(appData, "Autodesk", "C3D 2025", "esp", "Data", "IFC"),
            };

            foreach (var c in candidates)
                if (Directory.Exists(Path.GetDirectoryName(c))) return c;

            return candidates[0];
        }
    }
}
