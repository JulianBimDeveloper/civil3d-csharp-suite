using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Aec.PropertyData.DatabaseServices;

namespace XTS_Evolution_PB
{
    public static class Commands
    {
        // ============================================================
        // 1. AYUDANTES
        // ============================================================
        private static void Msg(string cmd) => Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n[XTS] Ejecutando: {cmd}...");
        
        // Normaliza un string para comparaciones: mayúsculas + quita acentos
        // "Sí" → "SI", "sí" → "SI", "N/a" → "N/A"
        private static string NormalizeForCompare(string input) {
            if (string.IsNullOrEmpty(input)) return "";
            var normalized = input.Trim().ToUpperInvariant();
            var sb = new System.Text.StringBuilder();
            foreach (char c in normalized.Normalize(System.Text.NormalizationForm.FormD))
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
        }

        private static string GetPropCI(JsonElement item, string key) {
            foreach (var prop in item.EnumerateObject())
                if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase)) return prop.Value.GetString()?.Trim() ?? "";
            return "";
        }

        // --- HELPERS PSET ---
        // outerTr: si ya hay una transacción abierta, pásala para evitar transacciones anidadas que
        // causan que GetObject devuelva silenciosamente cadenas vacías en AutoCAD .NET.
        private static string GetPsetValue(ObjectId objId, string psetName, string propName, Transaction outerTr = null) {
            bool ownsTransaction = outerTr == null;
            Transaction tr = outerTr;
            try {
                if (ownsTransaction) tr = objId.Database.TransactionManager.StartTransaction();

                var ent = tr.GetObject(objId, OpenMode.ForRead);
                var psetIds = PropertyDataServices.GetPropertySets(ent);
                foreach (ObjectId psetId in psetIds) {
                    var pset = tr.GetObject(psetId, OpenMode.ForRead) as PropertySet;
                    if (pset == null) continue;

                    var def = tr.GetObject(pset.PropertySetDefinition, OpenMode.ForRead) as PropertySetDefinition;
                    if (def != null && string.Equals(def.Name, psetName, StringComparison.OrdinalIgnoreCase)) {
                        foreach (PropertyDefinition propDef in def.Definitions) {
                            if (string.Equals(propDef.Name, propName, StringComparison.OrdinalIgnoreCase)) {
                                return pset.GetAt(propDef.Id)?.ToString() ?? "";
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex) {
                try {
                    var ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor;
                    ed?.WriteMessage($"\n⚠️ [GetPsetValue] {psetName}.{propName}: {ex.Message}");
                } catch { }
            }
            finally {
                if (ownsTransaction) tr?.Dispose();
            }
            return "";
        }

        private static bool SetPsetValue(ObjectId objId, string psetName, string propName, object value, Transaction tr) {
            try {
                var ent = tr.GetObject(objId, OpenMode.ForRead);
                var psetIds = PropertyDataServices.GetPropertySets(ent);
                
                foreach (ObjectId psetId in psetIds) {
                    var pset = tr.GetObject(psetId, OpenMode.ForWrite) as PropertySet;
                    if (pset == null) continue;

                    var def = tr.GetObject(pset.PropertySetDefinition, OpenMode.ForRead) as PropertySetDefinition;
                    if (def == null) continue;

                    // Si psetName es null, buscamos en todos. Si no, comparamos.
                    bool matchPset = string.IsNullOrEmpty(psetName) || string.Equals(def.Name, psetName, StringComparison.OrdinalIgnoreCase);
                    
                    if (matchPset) {
                        foreach (PropertyDefinition propDef in def.Definitions) {
                            // Búsqueda robusta: Ignora mayúsculas y acepta prefijos (ej: IFC::ifcExportAs coincide con ifcExportAs)
                            if (propDef.Name.IndexOf(propName, StringComparison.OrdinalIgnoreCase) >= 0) {
                                try {
                                    pset.SetAt(propDef.Id, value);
                                    return true;
                                }
                                catch (System.Exception setEx) {
                                    // Si falla (ej: es automática/solo lectura como 'Volume'), simplemente saltamos al siguiente
                                    try {
                                        var ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor;
                                        ed?.WriteMessage($"\n⚠️ [SetPsetValue] {psetName}.{propDef.Name}: {setEx.Message}");
                                    } catch { }
                                    return false;
                                }
                            }
                        }
                    }
                }
            } catch (System.Exception ex) {
                var ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
                ed.WriteMessage($"\n   ERR writing {propName}: {ex.Message}");
            }
            return false;
        }

        // ============================================================
        // 2. PANEL: ADMIN
        // ============================================================
        // 2. PANEL: ADMIN
        // El comando XTS_RELOAD ahora es gestionado por XTS_Loader.dll

        // ============================================================
        // 3. PANEL: MODEL PHASE
        // ============================================================
        [CommandMethod("XTS_ExportObjects")]
        public static async void XTS_ExportObjects() {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            ed.WriteMessage("\n🚀 Exportando parámetros de objetos a PocketBase...");

            try {
                // 1. Obtener ModelId
                string fileName = System.IO.Path.GetFileName(doc.Name);
                string modelId = await PocketBaseService.GetOrCreateRecord("models", "model_name", fileName);
                if (string.IsNullOrEmpty(modelId)) return;

                int totalScan = 0;
                int exported = 0;

                using (doc.LockDocument()) {
                    using (var tr = db.TransactionManager.StartTransaction()) {
                        var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                        var ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

                        foreach (ObjectId objId in ms) {
                            totalScan++;
                            var ent = tr.GetObject(objId, OpenMode.ForRead) as Entity;
                            if (ent == null) continue;

                            // Recopilar todos los Property Sets
                            var allPsets = new Dictionary<string, Dictionary<string, string>>();
                            var psetIds = PropertyDataServices.GetPropertySets(ent);
                            
                            if (psetIds.Count == 0) continue; // Solo exportamos objetos con datos

                            foreach (ObjectId psId in psetIds) {
                                var pset = tr.GetObject(psId, OpenMode.ForRead) as PropertySet;
                                var def = tr.GetObject(pset.PropertySetDefinition, OpenMode.ForRead) as PropertySetDefinition;
                                if (def == null) continue;

                                var props = new Dictionary<string, string>();
                                foreach (PropertyDefinition propDef in def.Definitions) {
                                    props[propDef.Name] = pset.GetAt(propDef.Id)?.ToString() ?? "";
                                }
                                allPsets[def.Name] = props;
                            }

                            // Upsert a PocketBase
                            var data = new {
                                model_id = modelId,
                                handle = ent.Handle.ToString(),
                                properties = allPsets 
                            };

                            bool success = await PocketBaseService.UpsertRecord("dwg_objects", data, $"model_id='{modelId}' && handle='{ent.Handle}'");
                            if (success) exported++;
                            
                            if (totalScan % 50 == 0) ed.WriteMessage($"\n📦 Procesando... ({totalScan} elementos)");
                        }
                        tr.Commit();
                    }
                }

                ed.WriteMessage($"\n✅ Exportación completada: {exported} objetos actualizados en la nube.");
                XtsCyberDialog.Show($"Exportación masiva Finalizada\n\nObjetos escaneados: {totalScan}\nEnviados a la nube: {exported}", "OBJ EXPORT COMPLETE", false, 6);

            } catch (System.Exception ex) {
                ed.WriteMessage($"\n❌ ERROR en ExportObjects: {ex.Message}");
            }
        }

        [CommandMethod("XTS_ImportObjects")]
        public static async void XTS_ImportObjects() {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            ed.WriteMessage("\n📥 Importando parámetros de objetos desde PocketBase...");

            try {
                // 1. Obtener ModelId
                string fileName = System.IO.Path.GetFileName(doc.Name);
                string modelId = await PocketBaseService.GetOrCreateRecord("models", "model_name", fileName);
                if (string.IsNullOrEmpty(modelId)) return;

                // 2. Traer registros de la nube (Paginación completa)
                var records = await PocketBaseService.GetAllRecordsAsync("dwg_objects", $"model_id='{modelId}'");
                if (records.Count == 0) {
                    ed.WriteMessage("\n⚠️ No se encontraron datos para este modelo en PocketBase.");
                    return;
                }
                ed.WriteMessage($"\n📦 Recibidos {records.Count} registros de la nube.");

                int imported = 0;
                using (doc.LockDocument()) {
                    using (var tr = db.TransactionManager.StartTransaction()) {
                        foreach (var record in records) {
                            string handleStr = GetPropCI(record, "handle");
                            long ln = Convert.ToInt64(handleStr, 16);
                            Handle h = new Handle(ln);
                            
                            if (db.TryGetObjectId(h, out ObjectId id)) {
                                var propsElement = record.GetProperty("properties");
                                // properties es un JSON: { "PsetName": { "Prop": "Val" } }
                                foreach (var psetObj in propsElement.EnumerateObject()) {
                                    string psetName = psetObj.Name;
                                    foreach (var propObj in psetObj.Value.EnumerateObject()) {
                                        // Usamos el motor de escritura robusto con Transaction sharing y Fuzzy matching
                                        SetPsetValue(id, psetName, propObj.Name, propObj.Value.GetString() ?? "", tr);
                                    }
                                }
                                imported++;
                            }
                        }
                        tr.Commit();
                    }
                }

                ed.WriteMessage($"\n✅ Importación completada: {imported} objetos actualizados en el DWG.");
                XtsCyberDialog.Show($"Importación Masiva Exitosa\n\nRegistros procesados: {records.Count}\nObjetos actualizados: {imported}", "OBJ IMPORT COMPLETE", false, 6);

            } catch (System.Exception ex) {
                ed.WriteMessage($"\n❌ ERROR en ImportObjects: {ex.Message}");
            }
        }

        [CommandMethod("XTS_MASTER_SYNC")]
        public static async void XTS_MasterSync() {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            ed.WriteMessage("\n🧠 Iniciando Sincronización Maestra de Parámetros (Inferencia total)...");

            try {
                // 1. Carga de Tablas Maestras en paralelo para velocidad
                var taskProducts = PocketBaseService.GetAllRecordsAsync("master_productos");
                var taskZones    = PocketBaseService.GetAllRecordsAsync("master_zonificacion");
                var taskStates   = PocketBaseService.GetAllRecordsAsync("master_estados");
                var taskDefaults = PocketBaseService.GetAllRecordsAsync("dwg_object_defaults");

                await Task.WhenAll(taskProducts, taskZones, taskStates, taskDefaults);

                var products       = taskProducts.Result;
                var zones          = taskZones.Result;
                var states         = taskStates.Result;
                var objectDefaults = taskDefaults.Result;

                ed.WriteMessage($"\n📚 Tablas cargadas: {products.Count} productos, {zones.Count} zonas, {states.Count} estados, {objectDefaults.Count} defaults.");

                if (products.Count == 0 && zones.Count == 0 && states.Count == 0) {
                    ed.WriteMessage("\n❌ ERROR: No se obtuvieron datos maestros de PocketBase. Verifica conexión y credenciales.");
                    XtsCyberDialog.Show("Sin datos maestros de PocketBase.\nVerifica que el servidor esté activo y las credenciales correctas.", "SYNC FAILED", false, 6);
                    return;
                }

                int totalCount = 0;
                int updatedCount = 0;

                using (doc.LockDocument()) {
                    using (var tr = db.TransactionManager.StartTransaction()) {
                        var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                        var ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

                        foreach (ObjectId objId in ms) {
                            var ent = tr.GetObject(objId, OpenMode.ForRead) as Entity;
                            if (ent == null) continue;
                            totalCount++;

                            // LLAVES MAESTRAS — se pasa tr para evitar transacciones anidadas
                            string codProd = GetPsetValue(objId, "ADIF_00_Generales", "ADIF_00_Codigo_Producto", tr).Trim();
                            if (string.IsNullOrEmpty(codProd)) codProd = GetPsetValue(objId, "ADIF_Atributos", "ADIF_00_Codigo_Producto", tr).Trim();

                            string zonif = GetPsetValue(objId, "ADIF_02_Localizacion", "ADIF_02_Zonificacion", tr).Trim();
                            string codEstado = GetPsetValue(objId, "ADIF_03_Coordinacion3D", "ADIF_03_Codigo_Estado", tr).Trim();

                            bool modified = false;

                            // 0. VALORES POR DEFECTO (desde dwg_object_defaults en PocketBase)
                            // Se aplican primero para que los masters puedan sobrescribirlos donde corresponda.
                            foreach (var def in objectDefaults) {
                                string pset  = GetPropCI(def, "pset_name");
                                string param = GetPropCI(def, "param_name");
                                string val   = GetPropCI(def, "param_value");
                                if (!string.IsNullOrEmpty(pset) && !string.IsNullOrEmpty(param))
                                    SetPsetValue(objId, pset, param, val, tr);
                            }

                            // 1. INFERENCIA DE PRODUCTO
                            if (!string.IsNullOrEmpty(codProd)) {
                                var p = products.FirstOrDefault(x => string.Equals(GetPropCI(x, "ADIF_00_Codigo_Producto"), codProd, StringComparison.OrdinalIgnoreCase));
                                if (p.ValueKind != JsonValueKind.Undefined) {
                                    SetPsetValue(objId, "ADIF_00_Generales", "ADIF_00_Descripcion_Producto", GetPropCI(p, "ADIF_00_Descripcion_Producto"), tr);
                                    SetPsetValue(objId, "ADIF_00_Generales", "ADIF_00_Disciplina", GetPropCI(p, "ADIF_00_Disciplina"), tr);
                                    SetPsetValue(objId, "ADIF_00_Generales", "ADIF_00_Subdisciplina", GetPropCI(p, "ADIF_00_Subdisciplina"), tr);
                                    SetPsetValue(objId, "ADIF_03_Coordinacion3D", "ADIF_03_Codigo_Estudio", GetPropCI(p, "ADIF_03_Codigo_Estudio"), tr);
                                    SetPsetValue(objId, "ADIF_05_5D", "ADIF_05_Unidad", GetPropCI(p, "ADIF_05_Unidad"), tr);

                                    // Lógica IFC Integrada
                                    string ifcClass = GetPropCI(p, "Exportar_a_IFC_como");
                                    string ifcType = GetPropCI(p, "Tipo_predefinido_de_IFC");
                                    if (!string.IsNullOrEmpty(ifcClass)) {
                                        string fullVal = ifcClass;
                                        if (!string.IsNullOrEmpty(ifcType)) {
                                            string t = ifcType.ToUpper();
                                            // Limpieza de subtipos no válidos
                                            if (t != "N/A" && t != "NOTDEFINED" && t != "NO TIENE SUBTIPOS") {
                                                fullVal += "." + t;
                                            }
                                        }
                                        SetPsetValue(objId, "IfcObject Properties", "ifcExportAs", fullVal, tr);
                                        SetPsetValue(objId, "", "ifcExportAs", fullVal, tr);
                                    }

                                    modified = true;
                                }
                            }

                            // 2. INFERENCIA DE ZONIFICACIÓN
                            if (!string.IsNullOrEmpty(zonif)) {
                                var z = zones.FirstOrDefault(x => string.Equals(GetPropCI(x, "ADIF_02_Zonificacion"), zonif, StringComparison.OrdinalIgnoreCase));
                                if (z.ValueKind != JsonValueKind.Undefined) {
                                    SetPsetValue(objId, "ADIF_02_Localizacion", "ADIF_02_Km_Inicio", GetPropCI(z, "ADIF_02_Km_Inicio"), tr);
                                    SetPsetValue(objId, "ADIF_02_Localizacion", "ADIF_02_Km_Fin", GetPropCI(z, "ADIF_02_Km_Fin"), tr);
                                    modified = true;
                                }
                            }

                            // 3. INFERENCIA DE ESTADO
                            if (!string.IsNullOrEmpty(codEstado)) {
                                var s = states.FirstOrDefault(x => string.Equals(GetPropCI(x, "ADIF_03_Codigo_Estado"), codEstado, StringComparison.OrdinalIgnoreCase));
                                if (s.ValueKind != JsonValueKind.Undefined) {
                                    SetPsetValue(objId, "ADIF_03_Coordinacion3D", "ADIF_03_Descripcion_Estado", GetPropCI(s, "ADIF_03_Descripcion_Estado"), tr);
                                    modified = true;
                                }
                            }

                            // 4. ID DEL OBJETO — siempre el handle actual del DWG
                            SetPsetValue(objId, "ADIF_00_Generales", "ADIF_00_ID_Objeto", ent.Handle.ToString(), tr);

                            if (modified) updatedCount++;
                        }
                        tr.Commit();
                    }
                }

                ed.WriteMessage($"\n✅ Sincronización Maestra Finalizada.");
                XtsCyberDialog.Show($"Master Sync Completado\n\nObjetos analizados: {totalCount}\nObjetos enriquecidos: {updatedCount}", "MASTER SYNC SUCCESS", false, 5);

            } catch (System.Exception ex) {
                ed.WriteMessage($"\n❌ ERROR en MasterSync: {ex.Message}");
            }
        }

        [CommandMethod("XTS_ExportInfoProyecto")]
        public static async void XTS_ExportInfoProyecto() {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            ed.WriteMessage("\n🚀 Iniciando Exportación de Información del Proyecto...");

            try {
                using (doc.LockDocument()) {
                    // 1. Recopilar Custom Properties del DWG
                    var customProps = new Dictionary<string, string>();
                    var summaryInfo = db.SummaryInfo;
                    var propsEnum = summaryInfo.CustomProperties;
                    while (propsEnum.MoveNext()) {
                        try {
                            var entry = propsEnum.Current;
                            // En AutoCAD 2025/C# moderno, usamos reflexión para evitar líos de tipos dinámists
                            string key = entry.GetType().GetProperty("Key")?.GetValue(entry, null)?.ToString();
                            string val = entry.GetType().GetProperty("Value")?.GetValue(entry, null)?.ToString();
                            if (!string.IsNullOrEmpty(key)) customProps[key] = val ?? "";
                        } catch { }
                    }

                    // 2. Obtener ModelId por nombre de archivo
                    string fileName = System.IO.Path.GetFileName(doc.Name);
                    ed.WriteMessage($"\n🔍 Buscando modelo '{fileName}' en PocketBase...");
                    string modelId = await PocketBaseService.GetOrCreateRecord("models", "model_name", fileName);

                    if (string.IsNullOrEmpty(modelId)) {
                        ed.WriteMessage("\n❌ ERROR: No se pudo obtener o crear el ModelId.");
                        return;
                    }

                    // 3. Preparar Data
                    var data = new {
                        model_id = modelId,
                        data = JsonSerializer.Serialize(customProps)
                    };

                    // 4. Upsert
                    bool success = await PocketBaseService.UpsertRecord("dwg_custom_properties", data, $"model_id='{modelId}'");

                    if (success) ed.WriteMessage("\n✅ Exportación exitosa.");
                    else ed.WriteMessage("\n❌ Fallo en Upsert.");
                }
            } catch (System.Exception ex) {
                ed.WriteMessage($"\n❌ Exception: {ex.Message}");
            }
        }

        [CommandMethod("XTS_ImportInfoProyecto")]
        public static async void XTS_ImportInfoProyecto() {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            ed.WriteMessage("\n📥 Iniciando Importación de Información del Proyecto...");

            try {
                using (doc.LockDocument()) {
                    string fileName = System.IO.Path.GetFileName(doc.Name);
                    string modelId = await PocketBaseService.GetOrCreateRecord("models", "model_name", fileName);

                    if (string.IsNullOrEmpty(modelId)) {
                        ed.WriteMessage("\n❌ ERROR: No se encontró el modelo.");
                        return;
                    }

                    string propsJson = await PocketBaseService.GetRecordsAsync("dwg_custom_properties", $"model_id='{modelId}'");
                    using var jdoc = JsonDocument.Parse(propsJson);
                    var items = jdoc.RootElement.GetProperty("items");
                    if (items.GetArrayLength() == 0) return;

                    var dataElement = items[0].GetProperty("data");
                    Dictionary<string, string> props;
                    if (dataElement.ValueKind == JsonValueKind.String) 
                        props = JsonSerializer.Deserialize<Dictionary<string, string>>(dataElement.GetString());
                    else 
                        props = JsonSerializer.Deserialize<Dictionary<string, string>>(dataElement.GetRawText());

                    int applied = 0;
                    using (var tr = db.TransactionManager.StartTransaction()) {
                        var builder = new DatabaseSummaryInfoBuilder(db.SummaryInfo);
                        foreach (var kvp in props) {
                            try { builder.CustomPropertyTable[kvp.Key] = kvp.Value; }
                            catch { try { builder.CustomPropertyTable.Add(kvp.Key, kvp.Value); } catch { } }
                            applied++;
                        }
                        db.SummaryInfo = builder.ToDatabaseSummaryInfo();
                        tr.Commit();
                    }
                    ed.WriteMessage($"\n✅ Importación exitosa: {applied} propiedades.");
                }
            } catch (System.Exception ex) {
                ed.WriteMessage($"\n❌ Exception: {ex.Message}");
            }
        }

        [CommandMethod("XTS_MirrorProjectInfo")]
        public static async void XTS_MirrorProjectInfo() {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            ed.WriteMessage("\n🔄 Sincronizando Información Maestra del Proyecto (dwg_project_defaults)...");

            try {
                using (doc.LockDocument()) {
                    // 1. Obtener todos los registros de dwg_project_defaults
                    // Usamos una consulta vacía para traer todos los registros maestros
                    string json = await PocketBaseService.GetRecordsAsync("dwg_project_defaults", "");
                    
                    if (string.IsNullOrEmpty(json) || json.StartsWith("ERROR:")) {
                        ed.WriteMessage($"\n❌ ERROR al conectar con PocketBase: {json}");
                        return;
                    }

                    using var jdoc = JsonDocument.Parse(json);
                    var items = jdoc.RootElement.GetProperty("items");
                    
                    if (items.GetArrayLength() == 0) {
                        ed.WriteMessage("\n⚠️ La tabla 'dwg_project_defaults' está vacía.");
                        return;
                    }

                    // 2. Aplicar propiedades al DWG
                    int applied = 0;
                    using (var tr = db.TransactionManager.StartTransaction()) {
                        // RECONSTRUCCION TOTAL: Creamos un builder limpio
                        var summaryInfo = db.SummaryInfo;
                        var builder = new DatabaseSummaryInfoBuilder(summaryInfo);

                        // Limpiar TODAS las propiedades actuales para evitar conflictos de raiz
                        var currentKeys = builder.CustomPropertyTable.GetEnumerator();
                        var keysToDelete = new System.Collections.Generic.List<string>();
                        while (currentKeys.MoveNext()) {
                            if (currentKeys.Key != null) keysToDelete.Add(currentKeys.Key.ToString());
                        }
                        foreach (var k in keysToDelete) builder.CustomPropertyTable.Remove(k);

                        // Insertar las nuevas de PocketBase
                        foreach (var item in items.EnumerateArray()) {
                            try {
                                string key = item.GetProperty("property_name").GetString();
                                string val = item.GetProperty("property_value").GetString();
                                
                                if (!string.IsNullOrEmpty(key)) {
                                    // DINAMISMO: Forzamos el nombre real del dibujo de forma robusta
                                    if (string.Equals(key.Trim(), "ADIF_00_Codigo_Archivo", StringComparison.OrdinalIgnoreCase)) {
                                        val = System.IO.Path.GetFileName(doc.Name);
                                    }
                                    
                                    builder.CustomPropertyTable.Add(key, val ?? "");
                                    applied++;
                                }
                            } catch { }
                        }

                        db.SummaryInfo = builder.ToDatabaseSummaryInfo();
                        tr.Commit();
                    }

                    ed.WriteMessage($"\n✅ Sync completado: {applied} propiedades maestras aplicadas.");
                }
            } catch (System.Exception ex) {
                ed.WriteMessage($"\n❌ Exception en Sync: {ex.Message}");
            }
        }

        // ============================================================
        // 5. PANEL: OBJECTS PARAMETERS
        // ============================================================
        [CommandMethod("XTS_HealthCheck")]
        public static async void XTS_HealthCheck() {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            ed.WriteMessage("\n🔍 Iniciando Super-Inspección de Calidad (Inquisidor ADIF)...");

            try {
                // 1. Carga de Tablas Maestras
                var taskProducts = PocketBaseService.GetAllRecordsAsync("master_productos");
                var taskZones = PocketBaseService.GetAllRecordsAsync("master_zonificacion");
                var taskStates = PocketBaseService.GetAllRecordsAsync("master_estados");
                var taskDefs = PocketBaseService.GetAllRecordsAsync("master_definicion");

                await Task.WhenAll(taskProducts, taskZones, taskStates, taskDefs);

                var products = taskProducts.Result;
                var zones = taskZones.Result;
                var states = taskStates.Result;
                var defs = taskDefs.Result;

                int redCount = 0;
                int orangeCount = 0;
                int greenCount = 0;
                var badIds = new ObjectIdCollection();

                using (doc.LockDocument()) {
                    using (var tr = db.TransactionManager.StartTransaction()) {
                        var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                        var ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

                        foreach (ObjectId objId in ms) {
                            var ent = tr.GetObject(objId, OpenMode.ForWrite) as Entity;
                            if (ent == null) continue;

                            // 1. OBTENER VALORES ACTUALES — se pasa tr para evitar transacciones anidadas
                            string cp = GetPsetValue(objId, "ADIF_00_Generales", "ADIF_00_Codigo_Producto", tr).Trim();
                            if (string.IsNullOrEmpty(cp)) cp = GetPsetValue(objId, "ADIF_Atributos", "ADIF_00_Codigo_Producto", tr).Trim();

                            string zn = GetPsetValue(objId, "ADIF_02_Localizacion", "ADIF_02_Zonificacion", tr).Trim();
                            string ce = GetPsetValue(objId, "ADIF_03_Coordinacion3D", "ADIF_03_Codigo_Estado", tr).Trim();
                            string fd = GetPsetValue(objId, "ADIF_03_Coordinacion3D", "ADIF_03_Fuente_Definicion", tr).Trim();

                            // 2. LOGICA ROJA (FALTAN RELLENAR OBLIGATORIOS)
                            if (string.IsNullOrEmpty(cp) || string.IsNullOrEmpty(zn) || string.IsNullOrEmpty(ce) || string.IsNullOrEmpty(fd)) {
                                ent.ColorIndex = 1; // ROJO
                                redCount++;
                                badIds.Add(objId);
                                continue;
                            }

                            bool isOrange = false;

                            // 3. LOGICA CODIGO PRODUCTO (4 PARTES Y EXISTENCIA)
                            var cpParts = cp.Split('.');
                            if (cpParts.Length != 4) {
                                isOrange = true;
                            } else {
                                var p = products.FirstOrDefault(x => string.Equals(GetPropCI(x, "ADIF_00_Codigo_Producto"), cp, StringComparison.OrdinalIgnoreCase));
                                if (p.ValueKind == JsonValueKind.Undefined) isOrange = true;
                                else {
                                    // Validar consistencia con otros campos obligatorios que dependen del producto
                                    if (GetPsetValue(objId, "ADIF_00_Generales", "ADIF_00_Descripcion_Producto", tr) != GetPropCI(p, "ADIF_00_Descripcion_Producto")) isOrange = true;
                                    if (GetPsetValue(objId, "ADIF_00_Generales", "ADIF_00_Disciplina", tr) != GetPropCI(p, "ADIF_00_Disciplina")) isOrange = true;
                                }
                            }

                            // 4. LOGICA ZONIFICACION
                            if (!isOrange) {
                                var z = zones.FirstOrDefault(x => string.Equals(GetPropCI(x, "ADIF_02_Zonificacion"), zn, StringComparison.OrdinalIgnoreCase));
                                if (z.ValueKind == JsonValueKind.Undefined) isOrange = true;
                            }

                            // 5. LOGICA ESTADO
                            if (!isOrange) {
                                var s = states.FirstOrDefault(x => string.Equals(GetPropCI(x, "ADIF_03_Codigo_Estado"), ce, StringComparison.OrdinalIgnoreCase));
                                if (s.ValueKind == JsonValueKind.Undefined) isOrange = true;
                            }

                            // 6. LOGICA DEFINICIÓN
                            if (!isOrange) {
                                var d = defs.FirstOrDefault(x => string.Equals(GetPropCI(x, "ADIF_03_Fuente_Definicion"), fd, StringComparison.OrdinalIgnoreCase));
                                if (d.ValueKind == JsonValueKind.Undefined) isOrange = true;
                            }

                            // 7. LOGICA ESTÁTICOS (N/A, SI)
                            // Normalizar: quitar acentos, pasar a mayúsculas para comparaciones robustas
                            if (!isOrange) {
                                string visRaw = GetPsetValue(objId, "ADIF_03_Coordinacion3D", "ADIF_03_Visibilidad_Temporal", tr);
                                string visNorm = NormalizeForCompare(visRaw);
                                if (visNorm != "SI" && visNorm != "S" && visNorm != "YES") isOrange = true;

                                string faseRaw = GetPsetValue(objId, "ADIF_04_4D", "ADIF_04_Fase", tr);
                                if (!string.Equals(faseRaw.Trim(), "N/A", StringComparison.OrdinalIgnoreCase)) isOrange = true;

                                if (GetPsetValue(objId, "ADIF_05_5D", "ADIF_05_Unidad", tr) == "") isOrange = true;
                                if (GetPsetValue(objId, "ADIF_05_5D", "ADIF_05_%Ejecucion", tr) == "") isOrange = true;
                            }

                            if (isOrange) {
                                ent.ColorIndex = 30; // NARANJA
                                orangeCount++;
                                badIds.Add(objId);
                            } else {
                                ent.ColorIndex = 3; // VERDE
                                greenCount++;
                            }
                        }
                        tr.Commit();
                    }
                }

                ed.WriteMessage($"\n✅ Inspección Finalizada.");
                ed.WriteMessage($"\n🟢 Correctos: {greenCount} | 🟠 Inconsistentes: {orangeCount} | 🔴 Incompletos: {redCount}");

                if (badIds.Count > 0) {
                    var opt = new PromptKeywordOptions($"\nSe han detectado {badIds.Count} errores. ¿Deseas seleccionarlos? [Si/No]", "Si No");
                    var res = ed.GetKeywords(opt);
                    if (res.Status == PromptStatus.OK && (res.StringResult == "Si" || res.StringResult == "S")) {
                        ed.SetImpliedSelection(badIds.Cast<ObjectId>().ToArray());
                    }
                }

            } catch (System.Exception ex) {
                ed.WriteMessage($"\n❌ ERROR en HealthCheck: {ex.Message}");
            }
        }

        [CommandMethod("XTS_StampHandles")]
        public static void XTS_StampHandles() {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            ed.WriteMessage("\n🔨 Estampando Handles en ADIF_00_ID_Objeto...");

            try {
                var selection = ed.GetSelection();
                if (selection.Status != PromptStatus.OK) return;

                int stamped = 0;
                using (doc.LockDocument()) {
                    foreach (SelectedObject obj in selection.Value) {
                        using (var tr = db.TransactionManager.StartTransaction()) {
                            var ent = tr.GetObject(obj.ObjectId, OpenMode.ForRead) as Entity;
                            if (ent != null) {
                                bool ok = SetPsetValue(obj.ObjectId, "ADIF_00_Generales", "ADIF_00_ID_Objeto", ent.Handle.ToString(), tr);
                                if (!ok) ok = SetPsetValue(obj.ObjectId, "ADIF_Atributos", "ADIF_00_ID_Objeto", ent.Handle.ToString(), tr);
                                if (ok) stamped++;
                            }
                            tr.Commit();
                        }
                    }
                }

                ed.WriteMessage($"\n✅ Estampado finalizado: {stamped} objetos actualizados.");
                XtsCyberDialog.Show("Estampado de IDs Completo", "STAMP IDS", false, 4);
            } catch (System.Exception ex) {
                ed.WriteMessage($"\n❌ ERROR en StampHandles: {ex.Message}");
            }
        }

        [CommandMethod("XTS_HealthReset")]
        public static void XTS_HealthReset() {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            using (doc.LockDocument()) {
                using (var tr = db.TransactionManager.StartTransaction()) {
                    var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    var ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

                    foreach (ObjectId id in ms) {
                        var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        if (ent != null) ent.ColorIndex = 256; // ByLayer
                    }
                    tr.Commit();
                }
            }
            ed.SetImpliedSelection(new ObjectId[0]);
            ed.WriteMessage("\n🧹 Visuales reseteados (Color ByLayer).");
            XtsCyberDialog.Show("Visuales Reseteados", "RESET COMPLETE", false, 2);
        }

        [CommandMethod("XTS_AuditPsets")]
        public static async void XTS_AuditPsets() {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            ed.WriteMessage("\n🔍 Escaneando estructura del modelo... Por favor, espera.");

            try {
                // 1. Obtener Estándar completo - SOLO TIPO OBJECT
                var masterParams = await PocketBaseService.GetAllRecordsAsync("master_adif_parameters", "Apply ~ 'Object'");
                if (masterParams.Count == 0) {
                    ed.WriteMessage("\n❌ ERROR: No se cargó el estándar de PocketBase o no hay parámetros de tipo 'Object'.");
                    return;
                }

                var expected = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in masterParams) {
                    string psName = GetPropCI(p, "pset_name");
                    string prName = GetPropCI(p, "property_name");
                    if (!expected.ContainsKey(psName)) expected[psName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    expected[psName].Add(prName);
                }

                // 2. Motor de Huella Digital de Errores (Con Inteligencia de Prefijos)
                var errorGroups = new Dictionary<string, ObjectIdCollection>();
                var perfectObjects = new ObjectIdCollection();

                using (var tr = db.TransactionManager.StartTransaction()) {
                    var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    var ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

                    foreach (ObjectId objId in ms) {
                        var ent = tr.GetObject(objId, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        var currentPsetIds = PropertyDataServices.GetPropertySets(ent);
                        var currentTree = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                        foreach (ObjectId psId in currentPsetIds) {
                            var pset = tr.GetObject(psId, OpenMode.ForRead) as PropertySet;
                            var def = tr.GetObject(pset.PropertySetDefinition, OpenMode.ForRead) as PropertySetDefinition;
                            if (def == null) continue;
                            var prps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (PropertyDefinition pDef in def.Definitions) prps.Add(pDef.Name);
                            currentTree[def.Name] = prps;
                        }

                        var missingList = new List<string>();
                        var excessList = new List<string>();
                        var namingIssues = new List<string>();

                        // --- A. Análisis contra Estándar ---
                        foreach (var exp in expected) {
                            if (!currentTree.ContainsKey(exp.Key)) {
                                missingList.Add($"[Falta Set: {exp.Key}]");
                            } else {
                                foreach (var p in exp.Value) {
                                    if (!currentTree[exp.Key].Contains(p)) {
                                        // ¿Existe algo parecido con otro prefijo?
                                        string pattern = p.Contains("_") ? p.Substring(p.IndexOf("_")+1) : p;
                                        var similar = currentTree[exp.Key].FirstOrDefault(x => x.EndsWith(pattern, StringComparison.OrdinalIgnoreCase));
                                        
                                        if (similar != null) namingIssues.Add($"[Nombre Mal: '{similar}' debería ser '{p}']");
                                        else missingList.Add($"[Falta Prop: {exp.Key}.{p}]");
                                    }
                                }
                            }
                        }

                        // --- B. Análisis de Excesos (Limpieza) ---
                        foreach (var cur in currentTree) {
                            if (string.Equals(cur.Key, "IfcObject Properties", StringComparison.OrdinalIgnoreCase)) continue;
                            bool isAdifOrBasura = cur.Key.StartsWith("ADIF", StringComparison.OrdinalIgnoreCase) || 
                                                 cur.Key.StartsWith("Ifc", StringComparison.OrdinalIgnoreCase) ||
                                                 cur.Key.Contains("Obra Lineal") || cur.Key.Contains("Civil");

                            if (isAdifOrBasura) {
                                if (!expected.ContainsKey(cur.Key)) excessList.Add($"[Basura Set: {cur.Key}]");
                                else {
                                    foreach (var p in cur.Value) {
                                        if (!expected[cur.Key].Contains(p)) {
                                            // Solo lo marcamos como exceso si no lo hemos detectado como naming issue arriba
                                            string pattern = p.Contains("_") ? p.Substring(p.IndexOf("_")+1) : p;
                                            bool isNaming = namingIssues.Any(x => x.Contains(p));
                                            if (!isNaming) excessList.Add($"[Basura Prop: {cur.Key}.{p}]");
                                        }
                                    }
                                }
                            }
                        }

                        // --- CLASIFICACIÓN ---
                        if (missingList.Count == 0 && excessList.Count == 0 && namingIssues.Count == 0) {
                            perfectObjects.Add(objId);
                        } else {
                            var allIssues = new List<string>();
                            allIssues.AddRange(missingList);
                            allIssues.AddRange(namingIssues);
                            allIssues.AddRange(excessList);
                            string signature = string.Join(" | ", allIssues);
                            if (!errorGroups.ContainsKey(signature)) errorGroups[signature] = new ObjectIdCollection();
                            errorGroups[signature].Add(objId);
                        }
                    }
                }

                // --- 3. MENU INTERACTIVO ---
                ed.WriteMessage("\n\n📊 AUDITORÍA ESTRUCTURAL AVANZADA:");
                ed.WriteMessage($"\n1. ✨ [PERFECTO] -> {perfectObjects.Count} objetos");
                
                var sorted = errorGroups.OrderByDescending(x => x.Value.Count).ToList();
                for (int i = 0; i < sorted.Count; i++) {
                    ed.WriteMessage($"\n{i+2}. {sorted[i].Key} -> ({sorted[i].Value.Count} objetos)");
                }
                ed.WriteMessage("\n0. Salir");

                var opt = new PromptIntegerOptions($"\nSelecciona (1-{sorted.Count+1}): ");
                opt.LowerLimit = 0; opt.UpperLimit = sorted.Count + 1;
                var res = ed.GetInteger(opt);

                if (res.Status == PromptStatus.OK && res.Value != 0) {
                    ObjectIdCollection selection = (res.Value == 1) ? perfectObjects : sorted[res.Value - 2].Value;
                    
                    if (selection.Count > 0) {
                        ed.SetImpliedSelection(selection.Cast<ObjectId>().ToArray());
                        ed.WriteMessage($"\n✅ Seleccionados {selection.Count} objetos.");
                        
                        // Detalle por consola si el usuario quiere
                        if (selection.Count < 200) {
                            ed.WriteMessage("\n📜 DETALLE DE ELEMENTOS (Handles):");
                            using (var tr = db.TransactionManager.StartTransaction()) {
                                foreach (ObjectId id in selection) {
                                    var ent = tr.GetObject(id, OpenMode.ForRead);
                                    ed.WriteMessage($"\n   - {ent.GetType().Name} (Handle: {id.Handle})");
                                }
                                tr.Commit();
                            }
                        }
                    }
                }




            } catch (System.Exception ex) {
                ed.WriteMessage($"\n❌ ERROR en AuditPsets: {ex.Message}");
            }
        }

        [CommandMethod("XTS_CheckPsets")] public static void XTS_CheckPsets() => XTS_SelectClean(); // Mismo fin: detectar fallos

        // ============================================================
        // 6. PANEL: OBJECTS IFC
        // ============================================================
        [CommandMethod("XTS_SYNC_IFC")]
        public static async void XTS_SyncIFC() {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            ed.WriteMessage("\n🏗️ Iniciando Sincronización IFC (Master Productos)...");

            try {
                // 1. Obtener TODOS los productos con la nueva función paginada
                var products = await PocketBaseService.GetAllRecordsAsync("master_productos");
                if (products.Count == 0) {
                    ed.WriteMessage("\n❌ ERROR: No se pudieron obtener productos de PocketBase.");
                    return;
                }
                ed.WriteMessage($"\n📦 Cargados {products.Count} productos maestros de la nube.");

                int count = 0;
                int updated = 0;
                var db = doc.Database;

                using (doc.LockDocument()) {
                    using (var tr = db.TransactionManager.StartTransaction()) {
                        var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                        var ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

                        foreach (ObjectId objId in ms) {
                            count++;
                            // Buscamos el código de producto
                            string codigo = GetPsetValue(objId, "ADIF_00_Generales", "ADIF_00_Codigo_Producto").Trim();
                            if (string.IsNullOrEmpty(codigo)) codigo = GetPsetValue(objId, "ADIF_Atributos", "ADIF_00_Codigo_Producto").Trim();
                            
                            if (string.IsNullOrEmpty(codigo)) continue;

                            // Buscar coincidencia (Ignorando mayúsculas/minúsculas)
                            var match = products.FirstOrDefault(p => 
                                string.Equals(GetPropCI(p, "ADIF_00_Codigo_Producto"), codigo, StringComparison.OrdinalIgnoreCase)
                            );
                            
                            if (match.ValueKind != JsonValueKind.Undefined) {
                                string ifcClass = GetPropCI(match, "Exportar_a_IFC_como");
                                string ifcType = GetPropCI(match, "Tipo_predefinido_de_IFC");

                                if (!string.IsNullOrEmpty(ifcClass)) {
                                    string fullVal = ifcClass;
                                    if (!string.IsNullOrEmpty(ifcType)) {
                                        string typeVal = ifcType.ToUpper();
                                        // Filtro de subtipos no válidos
                                        if (typeVal != "N/A" && typeVal != "NOTDEFINED" && typeVal != "NO TIENE SUBTIPOS") {
                                            fullVal += "." + typeVal;
                                        }
                                    }

                                    // Usamos la búsqueda robusta pasando la transacción actual
                                    bool ok = SetPsetValue(objId, "IfcObject Properties", "ifcExportAs", fullVal, tr);
                                    if (!ok) ok = SetPsetValue(objId, "", "ifcExportAs", fullVal, tr); // Segundo intento: buscar en CUALQUIER Pset
                                    
                                    if (ok) {
                                        updated++;
                                        // ed.WriteMessage($"\n✅ [{codigo}] -> {fullVal}");
                                    }
                                    else ed.WriteMessage($"\n⚠️ Propiedad 'ifcExportAs' no encontrada en objeto {objId.Handle}");
                                }
                            }
                        }
                        tr.Commit();
                    }
                }

                ed.WriteMessage($"\n✅ Proceso completado: {updated}/{count} objetos actualizados.");
                XtsCyberDialog.Show($"Sincronización IFC Finalizada\n\nObjetos procesados: {count}\nActualizados con éxito: {updated}", "SYNC IFC COMPLETE", false, 5);

            } catch (System.Exception ex) {
                ed.WriteMessage($"\n❌ ERROR en Sync IFC: {ex.Message}");
            }
        }

        [CommandMethod("XTS_CopyIFCName")]
        public static async void XTS_CopyIFCName() {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            string fileName = System.IO.Path.GetFileName(doc.Name);

            ed.WriteMessage($"\n🔍 Buscando nomenclaturas MIDP para: {fileName}...");

            try {
                // Buscamos mapeos que coincidan con el nombre del dibujo actual
                var records = await PocketBaseService.GetAllRecordsAsync("midp_mapping", $"native_name='{fileName}'");
                
                if (records.Count == 0) {
                    ed.WriteMessage("\n⚠️ No se encontraron mapeos para este archivo en 'midp_mapping'.");
                    XtsCyberDialog.Show($"No hay mapeos registrados para:\n{fileName}", "MIDP NOT FOUND", false, 4);
                    return;
                }

                // Extraer los nombres IFC únicos
                var names = records.Select(r => GetPropCI(r, "ifc_name")).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();

                if (names.Count == 0) {
                    ed.WriteMessage("\n⚠️ El mapeo existe pero no tiene 'ifc_name' asignado.");
                    return;
                }

                // Mostrar selector "bonito"
                string selected = XtsSelectorDialog.Show("Seleccionar Nombre IFC para Portapapeles", names);

                if (!string.IsNullOrEmpty(selected)) {
                    System.Windows.Clipboard.SetText(selected);
                    ed.WriteMessage($"\n📋 Copiado al portapapeles: {selected}");
                }

            } catch (System.Exception ex) {
                ed.WriteMessage($"\n❌ ERROR en CopyIFCName: {ex.Message}");
            }
        }

        [CommandMethod("XTS_ExportClean")] public static void XTS_ExportClean() => Msg("Export Clean");

        // ============================================================
        // 7. PANEL: NOMENCLATURE
        // ============================================================
        [CommandMethod("XTS_NomenclatureExport")] public static void XTS_NomExport() => Msg("Export (Nomenclature)");
        [CommandMethod("XTS_NomenclatureImport")] public static void XTS_NomImport() => Msg("Import (Nomenclature)");
        [CommandMethod("XTS_NomenclatureSync")] public static void XTS_NomSync() => Msg("Sync (Nomenclature)");

        // ============================================================
        // 8. PANEL: TOOLS
        // ============================================================
        [CommandMethod("XTS_SelectByProperty")]
        public static void XTS_SelectByProperty() {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            ed.WriteMessage("\n🔍 Iniciando Selector Jerárquico por Propiedad...");

            try {
                using (var tr = db.TransactionManager.StartTransaction()) {
                    // 1. Recopilar todos los objetos con Psets para analizar
                    var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    var ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

                    var allObjectsWithPsets = new List<ObjectId>();
                    foreach (ObjectId id in ms) {
                        var ent = tr.GetObject(id, OpenMode.ForRead);
                        if (PropertyDataServices.GetPropertySets(ent).Count > 0) allObjectsWithPsets.Add(id);
                    }

                    if (allObjectsWithPsets.Count == 0) {
                        ed.WriteMessage("\n⚠️ No se encontraron objetos con datos extendidos en el modelo.");
                        return;
                    }

                    // --- PASO 1: Seleccionar Conjunto (Pset) ---
                    var psetNames = new List<string>();
                    foreach (var id in allObjectsWithPsets) {
                        var ent = tr.GetObject(id, OpenMode.ForRead);
                        foreach (ObjectId psId in PropertyDataServices.GetPropertySets(ent)) {
                            var pset = tr.GetObject(psId, OpenMode.ForRead) as PropertySet;
                            var def = tr.GetObject(pset.PropertySetDefinition, OpenMode.ForRead) as PropertySetDefinition;
                            if (def != null && !psetNames.Contains(def.Name)) psetNames.Add(def.Name);
                        }
                    }
                    psetNames.Sort();

                    ed.WriteMessage("\n\n--- LISTADO DE CONJUNTOS DE PROPIEDADES ---");
                    for (int i = 0; i < psetNames.Count; i++) ed.WriteMessage($"\n [{i + 1}] {psetNames[i]}");
                    
                    var optPset = new PromptIntegerOptions("\n👉 Selecciona el NÚMERO del Conjunto: ");
                    optPset.AllowNone = false;
                    var resPset = ed.GetInteger(optPset);
                    if (resPset.Status != PromptStatus.OK || resPset.Value < 1 || resPset.Value > psetNames.Count) return;

                    string targetPsetName = psetNames[resPset.Value - 1];

                    // --- PASO 2: Seleccionar Propiedad ---
                    var propNames = new List<string>();
                    foreach (var id in allObjectsWithPsets) {
                        var ent = tr.GetObject(id, OpenMode.ForRead);
                        foreach (ObjectId psId in PropertyDataServices.GetPropertySets(ent)) {
                            var pset = tr.GetObject(psId, OpenMode.ForRead) as PropertySet;
                            var def = tr.GetObject(pset.PropertySetDefinition, OpenMode.ForRead) as PropertySetDefinition;
                            if (def != null && def.Name == targetPsetName) {
                                foreach (PropertyDefinition propDef in def.Definitions) {
                                    if (!propNames.Contains(propDef.Name)) propNames.Add(propDef.Name);
                                }
                            }
                        }
                    }
                    propNames.Sort();

                    ed.WriteMessage($"\n\n--- PROPIEDADES EN {targetPsetName} ---");
                    for (int i = 0; i < propNames.Count; i++) ed.WriteMessage($"\n [{i + 1}] {propNames[i]}");

                    var optProp = new PromptIntegerOptions("\n👉 Selecciona el NÚMERO de la Propiedad: ");
                    var resProp = ed.GetInteger(optProp);
                    if (resProp.Status != PromptStatus.OK || resProp.Value < 1 || resProp.Value > propNames.Count) return;

                    string targetPropName = propNames[resProp.Value - 1];

                    // --- PASO 3: Seleccionar Valor ---
                    var values = new Dictionary<string, List<ObjectId>>();
                    foreach (var id in allObjectsWithPsets) {
                        string val = GetPsetValue(id, targetPsetName, targetPropName);
                        if (!string.IsNullOrEmpty(val)) {
                            if (!values.ContainsKey(val)) values[val] = new List<ObjectId>();
                            values[val].Add(id);
                        }
                    }

                    var sortedValues = values.Keys.ToList();
                    sortedValues.Sort();

                    ed.WriteMessage($"\n\n--- VALORES PRESENTES PARA {targetPropName} ---");
                    for (int i = 0; i < sortedValues.Count; i++) ed.WriteMessage($"\n [{i + 1}] {sortedValues[i]} ({values[sortedValues[i]].Count} objetos)");

                    var optVal = new PromptIntegerOptions("\n👉 Selecciona el NÚMERO del Valor para SELECCIONAR: ");
                    var resVal = ed.GetInteger(optVal);
                    if (resVal.Status != PromptStatus.OK || resVal.Value < 1 || resVal.Value > sortedValues.Count) return;

                    string targetValue = sortedValues[resVal.Value - 1];

                    // --- FIN: Ejecutar Selección ---
                    var idsToSelect = values[targetValue].ToArray();
                    ed.SetImpliedSelection(idsToSelect);
                    ed.WriteMessage($"\n\n✅ SELECCIÓN COMPLETADA: {idsToSelect.Length} objetos seleccionados con {targetPropName} = '{targetValue}'.");
                }
            } catch (System.Exception ex) {
                ed.WriteMessage($"\n❌ ERROR en SelectByProperty: {ex.Message}");
            }
        }
        [CommandMethod("XTS_SelectClean")]
        public static async void XTS_SelectClean() {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            ed.WriteMessage("\n🧹 Buscando elementos 'limpios' (sin parámetros obligatorios ADIF)...");

            try {
                // 1. Obtener parámetros obligatorios de la nube
                var masterParams = await PocketBaseService.GetAllRecordsAsync("master_adif_parameters", "Type='object'");
                if (masterParams.Count == 0) {
                    ed.WriteMessage("\n⚠️ La tabla 'master_adif_parameters' está vacía o no tiene registros de tipo 'object'.");
                    return;
                }

                // Extraemos los nombres de Psets únicos que deberían estar presentes
                var requiredPsets = masterParams.Select(p => GetPropCI(p, "pset_name")).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();

                int totalScanned = 0;
                var idsToSelect = new List<ObjectId>();

                using (doc.LockDocument()) {
                    using (var tr = db.TransactionManager.StartTransaction()) {
                        var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                        var ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

                        foreach (ObjectId id in ms) {
                            totalScanned++;
                            // Ignorar si no es una entidad gráfica común
                            var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                            if (ent == null) continue;

                            // Obtener Psets actuales del objeto
                            var currentPsetIds = PropertyDataServices.GetPropertySets(ent);
                            var currentPsetNames = new List<string>();
                            foreach (ObjectId psId in currentPsetIds) {
                                var pset = tr.GetObject(psId, OpenMode.ForRead) as PropertySet;
                                var def = tr.GetObject(pset.PropertySetDefinition, OpenMode.ForRead) as PropertySetDefinition;
                                if (def != null) currentPsetNames.Add(def.Name.ToUpper());
                            }

                            // Verificar si le falta ALGUNO de los Psets obligatorios
                            bool isMissingSomething = false;
                            foreach (var req in requiredPsets) {
                                if (!currentPsetNames.Contains(req.ToUpper())) {
                                    isMissingSomething = true;
                                    break;
                                }
                            }

                            if (isMissingSomething) idsToSelect.Add(id);
                        }
                    }
                }

                if (idsToSelect.Count > 0) {
                    ed.SetImpliedSelection(idsToSelect.ToArray());
                    ed.WriteMessage($"\n\n✅ ESCANEO COMPLETADO: {idsToSelect.Count}/{totalScanned} objetos seleccionados por falta de parámetros.");
                    XtsCyberDialog.Show($"Auditoría de Limpieza\n\nElementos escaneados: {totalScanned}\nFaltos de parámetros: {idsToSelect.Count}\n\nSe han resaltado en el dibujo.", "AUDIT CLEAN", false, 5);
                } else {
                    ed.WriteMessage("\n✨ ¡Increíble! Todos los objetos cumplen con el estándar ADIF.");
                }

            } catch (System.Exception ex) {
                ed.WriteMessage($"\n❌ ERROR en SelectClean: {ex.Message}");
            }
        }
        [CommandMethod("XTS_SelectDuplicates")]
        public static void XTS_SelectDuplicates() {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            ed.WriteMessage("\n🔥 Iniciando detección de objetos duplicados geométricamente...");

            try {
                var duplicateIds = new List<ObjectId>();
                var spatialMap = new Dictionary<string, ObjectId>();

                using (var tr = db.TransactionManager.StartTransaction()) {
                    var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    var ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

                    int total = 0;
                    foreach (ObjectId id in ms) {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null || ent.IsErased) continue;
                        total++;

                        try {
                            // Generamos una "huella digital" del objeto basada en su geometría
                            // Combinamos: Tipo de Clase, Capa, y sus Extents (Caja de contorno)
                            var ext = ent.GeometricExtents;
                            string key = $"{ent.GetType().Name}|{ent.Layer}|{ext.MinPoint.X:F3},{ext.MinPoint.Y:F3},{ext.MinPoint.Z:F3}|{ext.MaxPoint.X:F3},{ext.MaxPoint.Y:F3},{ext.MaxPoint.Z:F3}";

                            if (spatialMap.ContainsKey(key)) {
                                duplicateIds.Add(id);
                            } else {
                                spatialMap[key] = id;
                            }
                        } catch {
                            // Algunos objetos sin geometría (ej: capas vacías o proxies raros) no tienen extents
                            continue;
                        }
                    }

                    if (duplicateIds.Count > 0) {
                        ed.SetImpliedSelection(duplicateIds.ToArray());
                        ed.WriteMessage($"\n\n✅ DETECCIÓN FINALIZADA: Se han encontrado {duplicateIds.Count} duplicados exactos.");
                        XtsCyberDialog.Show($"Detección de Duplicados\n\nObjetos analizados: {total}\nDuplicados encontrados: {duplicateIds.Count}\n\nLos duplicados han sido seleccionados. Puedes borrarlos con 'DELETE'.", "DUPLICATE FINDER", false, 6);
                    } else {
                        ed.WriteMessage("\n✨ El modelo está limpio. No se encontraron duplicados geométricos.");
                    }
                }
            } catch (System.Exception ex) {
                ed.WriteMessage($"\n❌ ERROR en SelectDuplicates: {ex.Message}");
            }
        }
        [CommandMethod("XTS_CopyAllParams")]
        public static void XTS_CopyAllParams() {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            ed.WriteMessage("\n📑 Iniciando Copia de Parámetros (Modo Espejo)...");

            try {
                // 1. Seleccionar Origen
                var resSource = ed.GetEntity("\n🎯 Selecciona el objeto ORIGEN: ");
                if (resSource.Status != PromptStatus.OK) return;

                // 2. Seleccionar Destinos
                var resTargets = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\n🎯 Selecciona los objetos DESTINO (uno o varios): " });
                if (resTargets.Status != PromptStatus.OK) return;

                using (doc.LockDocument()) {
                    int updatedCount = 0;

                    using (var tr = db.TransactionManager.StartTransaction()) {
                        // 3. Obtener Psets del Origen
                        var sourceEnt = tr.GetObject(resSource.ObjectId, OpenMode.ForRead);
                        var sourcePsetIds = PropertyDataServices.GetPropertySets(sourceEnt);
                        if (sourcePsetIds.Count == 0) {
                            ed.WriteMessage("\n⚠️ El objeto origen no tiene parámetros extendidos.");
                            return;
                        }

                        // 4. Iterar Destinos
                        foreach (SelectedObject targetObj in resTargets.Value) {
                            var destId = targetObj.ObjectId;
                            if (destId == resSource.ObjectId) continue; // No copiarse a sí mismo

                            // Limpiar Psets existentes en destino para "borrar lo distinto"
                            var destEnt = tr.GetObject(destId, OpenMode.ForWrite);
                            var currentDestPsets = PropertyDataServices.GetPropertySets(destEnt);
                            foreach (ObjectId oldPsetId in currentDestPsets) {
                                PropertyDataServices.RemovePropertySet(destEnt, oldPsetId);
                            }

                            // Clonar Psets del origen al destino
                            foreach (ObjectId sourcePsetId in sourcePsetIds) {
                                var sourcePset = tr.GetObject(sourcePsetId, OpenMode.ForRead) as PropertySet;
                                if (sourcePset == null) continue;

                                // Añadir la definición de Pset al destino
                                PropertyDataServices.AddPropertySet(destEnt, sourcePset.PropertySetDefinition);
                                
                                // Recuperar el nuevo Pset creado en destino para rellenar valores
                                var newDestPsets = PropertyDataServices.GetPropertySets(destEnt);
                                foreach (ObjectId newPsetId in newDestPsets) {
                                    var destPset = tr.GetObject(newPsetId, OpenMode.ForWrite) as PropertySet;
                                    if (destPset == null) continue;

                                    // Validar que es el mismo tipo de Pset (comparando definiciones)
                                    if (destPset.PropertySetDefinition == sourcePset.PropertySetDefinition) {
                                        
                                        var psetDef = tr.GetObject(sourcePset.PropertySetDefinition, OpenMode.ForRead) as PropertySetDefinition;
                                        if (psetDef != null) {
                                            foreach (PropertyDefinition propDef in psetDef.Definitions) {
                                                try {
                                                    // EXCEPCIÓN: ADIF_00_ID_Objeto (o el que contenga el Handle)
                                                    if (string.Equals(propDef.Name, "ADIF_00_ID_Objeto", StringComparison.OrdinalIgnoreCase) || 
                                                        string.Equals(propDef.Name, "Handle", StringComparison.OrdinalIgnoreCase)) {
                                                        
                                                        // Buscamos el Handle del destino
                                                        var targetEnt = tr.GetObject(destId, OpenMode.ForRead) as Entity;
                                                        destPset.SetAt(propDef.Id, targetEnt.Handle.ToString());
                                                    } else {
                                                        destPset.SetAt(propDef.Id, sourcePset.GetAt(propDef.Id));
                                                    }
                                                } catch { }
                                            }
                                        }
                                    }
                                }
                            }
                            updatedCount++;
                        }
                        tr.Commit();
                    }
                    ed.WriteMessage($"\n✅ Se han espejado los parámetros en {updatedCount} objetos.");
                }
            } catch (System.Exception ex) {
                ed.WriteMessage($"\n❌ ERROR en CopyAllParams: {ex.Message}");
            }
        }

        [CommandMethod("XTS_AuditModel")] public static void XTS_AuditModel() => Msg("Audit Cloud");
        [CommandMethod("XTS_ColorizeByStatus")] public static void XTS_ColorizeByStatus() => Msg("Colorize");

        // ============================================================
        // 9. PANEL: INFO (V2.2.1) & ADICIONALES
        // ============================================================
        [CommandMethod("XTS_SelectByHandle")] public static void XTS_SelectByHandle() {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            var res = ed.GetString("\n✍️ Introduce el Handle: ");
            if (res.Status == PromptStatus.OK) ed.WriteMessage($"\n🎯 Buscando {res.StringResult}...");
        }

        [CommandMethod("XTS_INFO")] public static void XTS_INFO() {
            XtsCyberDialog.Show("✅ XTS_Evolution_PB\nCore: Estable.\nAmbiente: " + Environment.GetEnvironmentVariable("XTS_PB_URL"), "SYSTEM INFO", false, 5);
        }

        // ============================================================
        // 10. PANEL: OBJECTS IFC — Workaround duplicate-name (v2.3.0)
        // ============================================================

        [CommandMethod("XTS_SETUP_IFC_MAPPING")]
        public static void XTS_SETUP_IFC_MAPPING() {
            var ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
            ed.WriteMessage("\n📦 Desplegando JSON de mapping IFC...");

            var result = IFC.IfcMappingDeployer.Deploy();

            if (!result.Success) {
                ed.WriteMessage($"\n❌ ERROR: {result.Error}");
                XtsCyberDialog.Show($"Fallo al desplegar mappings IFC:\n\n{result.Error}", "SETUP IFC MAPPING", false, 0);
                return;
            }

            string msg =
                $"✅ {result.Copied.Count} archivos desplegados\n\n" +
                $"Carpeta destino:\n{result.TargetFolder}\n\n" +
                $"Archivos:\n  • {string.Join("\n  • ", result.Copied)}\n\n" +
                (result.BackedUp.Count > 0 ? $"Backups creados ({result.BackedUp.Count}):\n  • {string.Join("\n  • ", result.BackedUp)}\n\n" : "") +
                "Reinicia el cuadro Export IFC en Civil 3D y selecciona estos JSON\n" +
                "como Property Set Mapping File para que se carguen.";

            ed.WriteMessage($"\n✅ Mappings desplegados en: {result.TargetFolder}");
            XtsCyberDialog.Show(msg, "SETUP IFC MAPPING — OK", false, 0);
        }

        [CommandMethod("XTS_INJECT_PROJECT_IFC")]
        public static void XTS_INJECT_PROJECT_IFC() {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            ed.WriteMessage("\n💉 Inject Project Params en IFC — workaround ADIF duplicate-name...");

            // 1. Recopilar Custom Props del DWG activo (mismo patrón que XTS_ExportInfoProyecto)
            var customProps = new Dictionary<string, string>();
            try {
                using (doc.LockDocument()) {
                    var summaryInfo = db.SummaryInfo;
                    var propsEnum = summaryInfo.CustomProperties;
                    while (propsEnum.MoveNext()) {
                        try {
                            var entry = propsEnum.Current;
                            string key = entry.GetType().GetProperty("Key")?.GetValue(entry, null)?.ToString();
                            string val = entry.GetType().GetProperty("Value")?.GetValue(entry, null)?.ToString();
                            if (!string.IsNullOrEmpty(key)) customProps[key] = val ?? "";
                        } catch { }
                    }
                }
            } catch (System.Exception ex) {
                ed.WriteMessage($"\n❌ Error leyendo Custom Props: {ex.Message}");
                return;
            }

            int adifCount = customProps.Count(kv => kv.Key != null && kv.Key.TrimStart().StartsWith("ADIF_", StringComparison.OrdinalIgnoreCase));
            ed.WriteMessage($"\n   📋 {customProps.Count} Custom Props leídas ({adifCount} ADIF_*).");

            if (adifCount == 0) {
                XtsCyberDialog.Show(
                    "El DWG activo no tiene Custom Properties ADIF_*.\n\n" +
                    "Ejecuta primero XTS_MirrorProjectInfo para sincronizar\n" +
                    "los parámetros maestros desde PocketBase.",
                    "INJECT PROJECT IFC", false, 0);
                return;
            }

            // 2. Pedir al usuario el .ifc destino
            string ifcPath;
            using (var dlg = new System.Windows.Forms.OpenFileDialog()) {
                dlg.Title = "Selecciona el archivo IFC exportado";
                dlg.Filter = "IFC files (*.ifc)|*.ifc|All files (*.*)|*.*";
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) {
                    ed.WriteMessage("\n   Cancelado por el usuario.");
                    return;
                }
                ifcPath = dlg.FileName;
            }

            ed.WriteMessage($"\n   📂 IFC: {ifcPath}");

            // 3. Inyectar
            var result = IFC.IfcProjectInjector.Inject(ifcPath, customProps);

            if (!result.Success) {
                ed.WriteMessage($"\n❌ ERROR en inyección: {result.Error}");
                XtsCyberDialog.Show(
                    $"Fallo al inyectar Psets de Proyecto:\n\n{result.Error}\n\n" +
                    (string.IsNullOrEmpty(result.BackupPath) ? "" : $"Backup en: {result.BackupPath}"),
                    "INJECT PROJECT IFC — ERROR", false, 0);
                return;
            }

            ed.WriteMessage($"\n✅ Schema {result.SchemaVersion} | {result.PsetsAdded} Psets | {result.PropertiesWritten} props | {result.Skipped} skipped.");
            XtsCyberDialog.Show(
                $"✅ Inyección OK\n\n" +
                $"Proyecto: {result.ProjectName}\n" +
                $"Schema:   {result.SchemaVersion}\n" +
                $"Psets añadidos: {result.PsetsAdded}\n" +
                $"Propiedades:    {result.PropertiesWritten}\n" +
                $"Skipped (vacíos): {result.Skipped}\n\n" +
                $"Backup: {result.BackupPath}",
                "INJECT PROJECT IFC — OK", false, 0);
        }

        // ============================================================
        // 11. PANEL: PIPE DATA — Set Material masivo con PickFirst
        // ============================================================

        [CommandMethod("XTS_SetMaterialStructures", CommandFlags.UsePickSet)]
        public static void XTS_SetMaterialStructures()
            => SetPartDataMaterial<Autodesk.Civil.DatabaseServices.Structure>("Estructuras",
                net => net.GetStructureIds());

        [CommandMethod("XTS_SetMaterialPipes", CommandFlags.UsePickSet)]
        public static void XTS_SetMaterialPipes()
            => SetPartDataMaterial<Autodesk.Civil.DatabaseServices.Pipe>("Tuberías",
                net => net.GetPipeIds());

        private static void SetPartDataMaterial<T>(
            string typeName,
            Func<Autodesk.Civil.DatabaseServices.Network, Autodesk.AutoCAD.DatabaseServices.ObjectIdCollection> getIds)
            where T : Autodesk.AutoCAD.DatabaseServices.Entity
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed  = doc.Editor;
            var db  = doc.Database;

            // ── 1. Selección: usar PickFirst o pedir al usuario ──
            Autodesk.AutoCAD.EditorInput.PromptSelectionResult selResult = ed.SelectImplied();
            bool hadPick = selResult.Status == Autodesk.AutoCAD.EditorInput.PromptStatus.OK
                           && selResult.Value != null && selResult.Value.Count > 0;
            if (!hadPick) {
                var opts = new Autodesk.AutoCAD.EditorInput.PromptSelectionOptions
                    { MessageForAdding = $"\nSelecciona las {typeName}: " };
                selResult = ed.GetSelection(opts);
                if (selResult.Status != Autodesk.AutoCAD.EditorInput.PromptStatus.OK) return;
            }

            // ── 2. Filtrar objetos del tipo correcto ──
            var targetIds = new List<Autodesk.AutoCAD.DatabaseServices.ObjectId>();
            using (var tr = db.TransactionManager.StartTransaction()) {
                foreach (Autodesk.AutoCAD.EditorInput.SelectedObject sel in selResult.Value) {
                    try {
                        if (tr.GetObject(sel.ObjectId, OpenMode.ForRead) is T)
                            targetIds.Add(sel.ObjectId);
                    } catch { }
                }
                tr.Abort();
            }

            if (targetIds.Count == 0) {
                ed.WriteMessage($"\n⚠️ No hay {typeName} en la selección actual.");
                XtsCyberDialog.Show($"No se encontraron {typeName} en la selección.", $"SET MATERIAL", false, 3);
                return;
            }
            ed.WriteMessage($"\n✔ {targetIds.Count} {typeName} seleccionados.");

            // ── 3. Recopilar materiales disponibles ──
            var allMaterials = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            try {
                // Primero: leer ValidValues del campo Material del primer elemento seleccionado
                using (var tr = db.TransactionManager.StartTransaction()) {
                    if (tr.GetObject(targetIds[0], OpenMode.ForRead) is T firstPart) {
                        var validVals = PartDataGetMaterialValidValues(firstPart);
                        foreach (var v in validVals) allMaterials.Add(v);
                    }
                    tr.Abort();
                }

                // Fallback: escanear todos los objetos de todas las redes para recoger los materiales en uso
                if (allMaterials.Count == 0) {
                    var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
                    var netIds   = civilDoc.GetPipeNetworkIds();
                    using (var tr = db.TransactionManager.StartTransaction()) {
                        foreach (Autodesk.AutoCAD.DatabaseServices.ObjectId netId in netIds) {
                            var network = tr.GetObject(netId, OpenMode.ForRead)
                                          as Autodesk.Civil.DatabaseServices.Network;
                            if (network == null) continue;
                            foreach (Autodesk.AutoCAD.DatabaseServices.ObjectId objId in getIds(network)) {
                                try {
                                    if (tr.GetObject(objId, OpenMode.ForRead) is T part) {
                                        string mat = PartDataReadMaterial(part);
                                        if (!string.IsNullOrWhiteSpace(mat)) allMaterials.Add(mat);
                                    }
                                } catch { }
                            }
                        }
                        tr.Abort();
                    }
                }
            } catch (System.Exception ex) {
                ed.WriteMessage($"\n  ⚠️ Error escaneando materiales: {ex.Message}");
            }

            // Si no encontramos ninguno, ponemos un placeholder
            if (allMaterials.Count == 0) allMaterials.Add("Material sin definir");

            // ── 4. Mostrar selector de material ──
            string picked = XtsSelectorDialog.Show(
                $"Material — {typeName} ({targetIds.Count} seleccionados)",
                new List<string>(allMaterials));
            if (string.IsNullOrEmpty(picked)) return;

            // ── 5. Aplicar material a todos los seleccionados ──
            int ok = 0, fail = 0;
            using (var tr = db.TransactionManager.StartTransaction()) {
                foreach (var objId in targetIds) {
                    try {
                        if (tr.GetObject(objId, OpenMode.ForWrite) is T part) {
                            if (PartDataWriteMaterial(part, picked)) ok++;
                            else fail++;
                        }
                    } catch (System.Exception ex) {
                        ed.WriteMessage($"\n  ⚠️ {objId}: {ex.Message}");
                        fail++;
                    }
                }
                tr.Commit();
            }

            string summary = $"✅ '{picked}' → {ok} {typeName} actualizados"
                           + (fail > 0 ? $"\n⚠️ {fail} no se pudieron actualizar" : "");
            ed.WriteMessage($"\n{summary}");
            XtsCyberDialog.Show(summary, $"SET MATERIAL — {typeName.ToUpper()}", false, 4);
        }

        // Obtiene los valores válidos del campo Material — diagnóstico incluido
        private static List<string> PartDataGetMaterialValidValues(Autodesk.AutoCAD.DatabaseServices.Entity part) {
            var result = new List<string>();
            var ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor;
            try {
                var partDataProp = part.GetType().GetProperty("PartData");
                if (partDataProp == null) { ed?.WriteMessage("\n[DIAG] PartData prop: NULL"); return result; }
                var partData = partDataProp.GetValue(part);
                if (partData == null) { ed?.WriteMessage("\n[DIAG] PartData value: NULL"); return result; }

                ed?.WriteMessage($"\n[DIAG] PartData type: {partData.GetType().FullName}");

                object field = null;

                var partContextType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return System.Array.Empty<Type>(); } })
                    .FirstOrDefault(t => t.Name == "PartContextType");
                if (partContextType != null && Enum.IsDefined(partContextType, "Material")) {
                    var materialEnum = Enum.Parse(partContextType, "Material");
                    var getFieldMethod = partData.GetType().GetMethod("GetDataFieldBy", new[] { partContextType });
                    if (getFieldMethod != null)
                        field = getFieldMethod.Invoke(partData, new[] { materialEnum });
                }

                if (field == null) {
                    var fieldsProp = partData.GetType().GetProperty("Fields")
                                  ?? partData.GetType().GetProperty("Items");
                    if (fieldsProp?.GetValue(partData) is System.Collections.IEnumerable fields) {
                        foreach (var f in fields) {
                            var nameProp = f.GetType().GetProperty("Name") ?? f.GetType().GetProperty("FieldName");
                            if (nameProp?.GetValue(f)?.ToString()
                                         .IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0) {
                                field = f;
                                break;
                            }
                        }
                    }
                }

                if (field == null) { ed?.WriteMessage("\n[DIAG] Material field: NULL"); return result; }

                ed?.WriteMessage($"\n[DIAG] Field type: {field.GetType().FullName}");
                // Log ALL properties of the field object
                foreach (var p in field.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)) {
                    try {
                        var val = p.GetValue(field);
                        ed?.WriteMessage($"\n[DIAG]   .{p.Name} ({p.PropertyType.Name}) = {val}");
                    } catch (System.Exception ex) {
                        ed?.WriteMessage($"\n[DIAG]   .{p.Name} ERROR: {ex.Message}");
                    }
                }

                // Intentar múltiples nombres de propiedad para valores válidos
                string[] candidateProps = { "ValidValues", "AllowedValues", "ValueList", "Choices", "AvailableValues", "EnumValues", "Values" };
                foreach (var propName in candidateProps) {
                    var prop = field.GetType().GetProperty(propName);
                    if (prop == null) continue;
                    if (prop.GetValue(field) is System.Collections.IEnumerable vals) {
                        foreach (var v in vals) {
                            var str = v?.ToString();
                            if (!string.IsNullOrWhiteSpace(str)) result.Add(str);
                        }
                        if (result.Count > 0) {
                            ed?.WriteMessage($"\n[DIAG] Got {result.Count} valid values from .{propName}");
                            return result;
                        }
                    }
                }
                ed?.WriteMessage($"\n[DIAG] No valid values found in any candidate property");
            } catch (System.Exception ex) {
                ed?.WriteMessage($"\n[DIAG] Exception: {ex.Message}");
            }
            return result;
        }

        // Lee el campo Material de Datos de Pieza vía reflexión (PartData API)
        private static string PartDataReadMaterial(Autodesk.AutoCAD.DatabaseServices.Entity part) {
            try {
                var partDataProp = part.GetType().GetProperty("PartData");
                if (partDataProp == null) return "";
                var partData = partDataProp.GetValue(part);
                if (partData == null) return "";

                // Buscar PartContextType.Material entre los ensamblados cargados
                var partContextType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return System.Array.Empty<Type>(); } })
                    .FirstOrDefault(t => t.Name == "PartContextType");

                if (partContextType != null && Enum.IsDefined(partContextType, "Material")) {
                    var materialEnum = Enum.Parse(partContextType, "Material");
                    var getFieldMethod = partData.GetType()
                        .GetMethod("GetDataFieldBy", new[] { partContextType });
                    if (getFieldMethod != null) {
                        var field = getFieldMethod.Invoke(partData, new[] { materialEnum });
                        return field?.GetType().GetProperty("Value")?.GetValue(field)?.ToString() ?? "";
                    }
                }

                // Fallback: buscar cualquier campo llamado "Material" en la colección
                var fieldsProp = partData.GetType().GetProperty("Fields")
                              ?? partData.GetType().GetProperty("Items");
                if (fieldsProp != null && fieldsProp.GetValue(partData) is System.Collections.IEnumerable fields) {
                    foreach (var f in fields) {
                        var nameProp  = f.GetType().GetProperty("Name") ?? f.GetType().GetProperty("FieldName");
                        var valueProp = f.GetType().GetProperty("Value");
                        if (nameProp?.GetValue(f)?.ToString()
                                    .IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0)
                            return valueProp?.GetValue(f)?.ToString() ?? "";
                    }
                }
            } catch { }
            return "";
        }

        // Escribe el campo Material de Datos de Pieza vía reflexión
        private static bool PartDataWriteMaterial(Autodesk.AutoCAD.DatabaseServices.Entity part, string material) {
            try {
                var partDataProp = part.GetType().GetProperty("PartData");
                if (partDataProp == null) return false;
                var partData = partDataProp.GetValue(part);
                if (partData == null) return false;

                var partContextType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return System.Array.Empty<Type>(); } })
                    .FirstOrDefault(t => t.Name == "PartContextType");

                if (partContextType != null && Enum.IsDefined(partContextType, "Material")) {
                    var materialEnum  = Enum.Parse(partContextType, "Material");
                    var getFieldMethod = partData.GetType()
                        .GetMethod("GetDataFieldBy", new[] { partContextType });
                    if (getFieldMethod != null) {
                        var field = getFieldMethod.Invoke(partData, new[] { materialEnum });
                        if (field != null) {
                            var valueProp = field.GetType().GetProperty("Value");
                            if (valueProp != null && valueProp.CanWrite) {
                                valueProp.SetValue(field, material);
                                // Reasignar PartData al objeto para que persista
                                if (partDataProp.CanWrite) partDataProp.SetValue(part, partData);
                                return true;
                            }
                        }
                    }
                }

                // Fallback: buscar campo "Material" en colección y escribirlo
                var fieldsProp = partData.GetType().GetProperty("Fields")
                              ?? partData.GetType().GetProperty("Items");
                if (fieldsProp != null && fieldsProp.GetValue(partData) is System.Collections.IEnumerable fields) {
                    foreach (var f in fields) {
                        var nameProp  = f.GetType().GetProperty("Name") ?? f.GetType().GetProperty("FieldName");
                        var valueProp = f.GetType().GetProperty("Value");
                        if (nameProp?.GetValue(f)?.ToString()
                                    .IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0
                            && valueProp != null && valueProp.CanWrite) {
                            valueProp.SetValue(f, material);
                            if (partDataProp.CanWrite) partDataProp.SetValue(part, partData);
                            return true;
                        }
                    }
                }
            } catch { }
            return false;
        }

        // ============================================================
        // 12. PANEL: PIPE RULES — Apply Rules masivo a redes de tuberías
        // ============================================================

        [CommandMethod("XTS_ApplyRulesPipes")]
        public static void XTS_ApplyRulesPipes() {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            ed.WriteMessage("\n⚙️ [XTS] Apply Rules — Tuberías...");

            try {
                var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
                var netIds = civilDoc.GetPipeNetworkIds();

                if (netIds == null || netIds.Count == 0) {
                    ed.WriteMessage("\n⚠️ No se encontraron redes de tuberías en el dibujo.");
                    XtsCyberDialog.Show("No hay redes de tuberías en el dibujo activo.", "APPLY RULES — TUBERÍAS", false, 3);
                    return;
                }

                int totalOk = 0, totalFail = 0;

                // Procesar red por red — una transacción por red para no saturar la pila de deshacer
                foreach (Autodesk.AutoCAD.DatabaseServices.ObjectId netId in netIds) {
                    using (var tr = db.TransactionManager.StartTransaction()) {
                        var network = tr.GetObject(netId, OpenMode.ForRead) as Autodesk.Civil.DatabaseServices.Network;
                        if (network == null) { tr.Abort(); continue; }

                        var pipeIds = network.GetPipeIds();
                        ed.WriteMessage($"\n  ↳ Red '{network.Name}': {pipeIds.Count} tuberías");

                        foreach (Autodesk.AutoCAD.DatabaseServices.ObjectId pipeId in pipeIds) {
                            try {
                                var pipe = tr.GetObject(pipeId, OpenMode.ForWrite) as Autodesk.Civil.DatabaseServices.Pipe;
                                if (pipe == null) continue;
                                pipe.ApplyRules();
                                totalOk++;
                            } catch (System.Exception pEx) {
                                ed.WriteMessage($"\n    ⚠️ {pipeId}: {pEx.Message}");
                                totalFail++;
                            }
                        }
                        tr.Commit();
                    }
                }

                string summary = $"✅ {totalOk} tuberías actualizadas" + (totalFail > 0 ? $"\n⚠️ {totalFail} con error (sin reglas asignadas)" : "");
                ed.WriteMessage($"\n{summary}");
                XtsCyberDialog.Show(summary, "APPLY RULES — TUBERÍAS", false, 4);

            } catch (System.Exception ex) {
                ed.WriteMessage($"\n❌ ERROR: {ex.Message}");
                XtsCyberDialog.Show($"Error inesperado:\n{ex.Message}", "APPLY RULES — ERROR", false, 0);
            }
        }

        [CommandMethod("XTS_ApplyRulesStructures")]
        public static void XTS_ApplyRulesStructures() {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            ed.WriteMessage("\n⚙️ [XTS] Apply Rules — Estructuras...");

            try {
                var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
                var netIds = civilDoc.GetPipeNetworkIds();

                if (netIds == null || netIds.Count == 0) {
                    ed.WriteMessage("\n⚠️ No se encontraron redes de tuberías en el dibujo.");
                    XtsCyberDialog.Show("No hay redes de tuberías en el dibujo activo.", "APPLY RULES — ESTRUCTURAS", false, 3);
                    return;
                }

                int totalOk = 0, totalFail = 0;

                foreach (Autodesk.AutoCAD.DatabaseServices.ObjectId netId in netIds) {
                    using (var tr = db.TransactionManager.StartTransaction()) {
                        var network = tr.GetObject(netId, OpenMode.ForRead) as Autodesk.Civil.DatabaseServices.Network;
                        if (network == null) { tr.Abort(); continue; }

                        var strucIds = network.GetStructureIds();
                        ed.WriteMessage($"\n  ↳ Red '{network.Name}': {strucIds.Count} estructuras");

                        foreach (Autodesk.AutoCAD.DatabaseServices.ObjectId strucId in strucIds) {
                            try {
                                var struc = tr.GetObject(strucId, OpenMode.ForWrite) as Autodesk.Civil.DatabaseServices.Structure;
                                if (struc == null) continue;
                                struc.ApplyRules();
                                totalOk++;
                            } catch (System.Exception sEx) {
                                ed.WriteMessage($"\n    ⚠️ {strucId}: {sEx.Message}");
                                totalFail++;
                            }
                        }
                        tr.Commit();
                    }
                }

                string summary = $"✅ {totalOk} estructuras actualizadas" + (totalFail > 0 ? $"\n⚠️ {totalFail} con error (sin reglas asignadas)" : "");
                ed.WriteMessage($"\n{summary}");
                XtsCyberDialog.Show(summary, "APPLY RULES — ESTRUCTURAS", false, 4);

            } catch (System.Exception ex) {
                ed.WriteMessage($"\n❌ ERROR: {ex.Message}");
                XtsCyberDialog.Show($"Error inesperado:\n{ex.Message}", "APPLY RULES — ERROR", false, 0);
            }
        }
    }
}
