using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.Ifc4.Kernel;
using Xbim.Ifc4.MeasureResource;
using Xbim.Ifc4.PropertyResource;

namespace XTS_Evolution_PB.IFC
{
    public sealed record IfcInjectionResult(
        bool Success,
        string ProjectName,
        int PsetsAdded,
        int PropertiesWritten,
        int Skipped,
        string SchemaVersion,
        string BackupPath,
        string Error
    );

    public static class IfcProjectInjector
    {
        public static IfcInjectionResult Inject(string ifcPath, IDictionary<string, string> dwgCustomProps)
        {
            if (!File.Exists(ifcPath))
                return new IfcInjectionResult(false, "", 0, 0, 0, "", "", $"Archivo no existe: {ifcPath}");

            string backupPath = ifcPath + ".bak";
            try { File.Copy(ifcPath, backupPath, overwrite: true); }
            catch (Exception ex) { return new IfcInjectionResult(false, "", 0, 0, 0, "", "", $"No se pudo crear backup: {ex.Message}"); }

            try
            {
                using var model = IfcStore.Open(ifcPath);
                var schema = model.SchemaVersion.ToString();

                if (model.SchemaVersion != XbimSchemaVersion.Ifc4 &&
                    model.SchemaVersion != XbimSchemaVersion.Ifc4x1 &&
                    model.SchemaVersion != XbimSchemaVersion.Ifc4x3)
                {
                    return new IfcInjectionResult(false, "", 0, 0, 0, schema, backupPath,
                        $"Schema {schema} no soportado por este injector (espera IFC4 / IFC4x3).");
                }

                var project = model.Instances.OfType<IfcProject>().FirstOrDefault();
                if (project == null)
                    return new IfcInjectionResult(false, "", 0, 0, 0, schema, backupPath, "El IFC no contiene IfcProject.");

                int psetsAdded = 0, propsWritten = 0, skipped = 0;
                string projectName = project.Name?.ToString() ?? "(sin nombre)";

                using (var txn = model.BeginTransaction("Inject ADIF Project Psets"))
                {
                    foreach (var group in ProjectParamsMap.GroupedByPset())
                    {
                        var entries = group.ToList();
                        var values = entries
                            .Select(e => new {
                                Entry = e,
                                Value = TryGetValue(dwgCustomProps, e.CustomPropName)
                            })
                            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                            .ToList();

                        skipped += entries.Count - values.Count;
                        if (values.Count == 0) continue;

                        var pset = model.Instances.New<IfcPropertySet>(ps =>
                        {
                            ps.Name = group.Key;
                            ps.Description = "Inyectado por XTS Suite (workaround duplicate-name)";
                            foreach (var v in values)
                            {
                                var prop = model.Instances.New<IfcPropertySingleValue>(p =>
                                {
                                    p.Name = v.Entry.PropertyName;
                                    p.NominalValue = new IfcLabel(v.Value);
                                });
                                ps.HasProperties.Add(prop);
                                propsWritten++;
                            }
                        });

                        model.Instances.New<IfcRelDefinesByProperties>(rel =>
                        {
                            rel.Name = $"ADIF_PROJECT_{group.Key}";
                            rel.RelatingPropertyDefinition = pset;
                            rel.RelatedObjects.Add(project);
                        });

                        psetsAdded++;
                    }

                    txn.Commit();
                }

                model.SaveAs(ifcPath);

                return new IfcInjectionResult(true, projectName, psetsAdded, propsWritten, skipped, schema, backupPath, null);
            }
            catch (Exception ex)
            {
                try { File.Copy(backupPath, ifcPath, overwrite: true); } catch { }
                return new IfcInjectionResult(false, "", 0, 0, 0, "", backupPath, ex.Message);
            }
        }

        private static string TryGetValue(IDictionary<string, string> map, string key)
        {
            if (map == null) return null;
            if (map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) return v.Trim();
            var hit = map.FirstOrDefault(kv =>
                string.Equals(kv.Key?.Trim(), key, StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(hit.Value) ? null : hit.Value.Trim();
        }
    }
}
