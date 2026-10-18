using System;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.Windows;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;

[assembly: ExtensionApplication(typeof(XTS_Evolution_PB.App))]

namespace XTS_Evolution_PB
{
    using AutoCADApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

    public class App : IExtensionApplication
    {
        public const string Version = "2.3.0";
        public static string LastUpdate = DateTime.Now.ToString("HH:mm");
        private static bool isRibbonCreated = false;

        public void Initialize()
        {
            McpBridge.Start();
            if (ComponentManager.Ribbon == null)
                AutoCADApp.Idle += OnIdle;
            else
            {
                var tab = ComponentManager.Ribbon.Tabs.FirstOrDefault(t => t.Id == "XTS_SUITE_TAB");
                if (tab != null) ComponentManager.Ribbon.Tabs.Remove(tab);
                CreateRibbon();
            }
        }

        public void Terminate() { McpBridge.Stop(); }

        private void OnIdle(object sender, EventArgs e)
        {
            if (ComponentManager.Ribbon != null)
            {
                AutoCADApp.Idle -= OnIdle;
                CreateRibbon();
            }
        }

        public void CreateRibbon()
        {
            var ribbonControl = ComponentManager.Ribbon;
            if (ribbonControl == null) return;

            var tab = ribbonControl.Tabs.FirstOrDefault(t => t.Id == "XTS_SUITE_TAB");
            if (tab == null)
            {
                tab = new RibbonTab { Title = "XTS Suite", Id = "XTS_SUITE_TAB" };
                ribbonControl.Tabs.Add(tab);
            }
            else
            {
                tab.Panels.Clear();
            }

            // 0. ADMIN - SOLO SYNC
            var sourceAdmin = new RibbonPanelSource { Title = "Admin" };
            var panelAdmin = new RibbonPanel { Source = sourceAdmin };
            tab.Panels.Add(panelAdmin);
            sourceAdmin.Items.Add(CreateBigButton("Sync Add-in", "XTS_RELOAD", Utils.IconType.Sync, "Recarga el plugin."));

            // 2. PROJECT INFO
            var sourceInfo = new RibbonPanelSource { Title = "Project Information" };
            var panelInfo = new RibbonPanel { Source = sourceInfo };
            tab.Panels.Add(panelInfo);
            sourceInfo.Items.Add(CreateBigButton("Export", "XTS_ExportInfoProyecto", Utils.IconType.Export, "Exporta info."));
            sourceInfo.Items.Add(CreateBigButton("Import", "XTS_ImportInfoProyecto", Utils.IconType.Import, "Importa info."));
            sourceInfo.Items.Add(CreateBigButton("Sync", "XTS_MirrorProjectInfo", Utils.IconType.Sync, "Sincroniza info."));

            // 3. OBJECTS PARAMETERS
            var sourceObj = new RibbonPanelSource { Title = "Objects Parameters" };
            var panelObj = new RibbonPanel { Source = sourceObj };
            tab.Panels.Add(panelObj);
            sourceObj.Items.Add(CreateBigButton("Export", "XTS_ExportObjects", Utils.IconType.Export, "Exporta parámetros."));
            sourceObj.Items.Add(CreateBigButton("Import", "XTS_ImportObjects", Utils.IconType.Import, "Importa parámetros."));
            sourceObj.Items.Add(CreateBigButton("Sync", "XTS_MASTER_SYNC", Utils.IconType.Sync, "Sincroniza parámetros."));
            sourceObj.Items.Add(CreateBigButton("Check", "XTS_HealthCheck", Utils.IconType.Check, "Chequea IDs."));
            sourceObj.Items.Add(CreateBigButton("Audit Psets", "XTS_AuditPsets", Utils.IconType.Database, "Audit Psets."));
            sourceObj.Items.Add(CreateBigButton("Reset", "XTS_HealthReset", Utils.IconType.Reset, "Reset visual."));
            sourceObj.Items.Add(CreateBigButton("Stamp IDs", "XTS_StampHandles", Utils.IconType.ID, "Stamp Handles."));

            // 4. OBJECTS IFC
            var sourceIFC = new RibbonPanelSource { Title = "Objects IFC" };
            var panelIFC = new RibbonPanel { Source = sourceIFC };
            tab.Panels.Add(panelIFC);
            sourceIFC.Items.Add(CreateBigButton("Sync IFC", "XTS_SYNC_IFC", Utils.IconType.Sync, "Sync IFC Data."));
            sourceIFC.Items.Add(CreateBigButton("Copy Name", "XTS_CopyIFCName", Utils.IconType.Copy, "Copy IFC Name."));
            sourceIFC.Items.Add(CreateBigButton("Export", "XTS_ExportClean", Utils.IconType.Export, "Export Clean IFC."));
            sourceIFC.Items.Add(new RibbonSeparator());
            sourceIFC.Items.Add(CreateBigButton("Setup\nMapping", "XTS_SETUP_IFC_MAPPING", Utils.IconType.Database, "Despliega los 3 JSON de IFC Export al AppData de Civil 3D."));
            sourceIFC.Items.Add(CreateBigButton("Inject\nProject", "XTS_INJECT_PROJECT_IFC", Utils.IconType.ID, "Inyecta Psets de Proyecto en IFC ya exportado (workaround duplicate-name)."));

            // 5. NOMENCLATURE
            var sourceNom = new RibbonPanelSource { Title = "Nomenclature" };
            var panelNom = new RibbonPanel { Source = sourceNom };
            tab.Panels.Add(panelNom);
            sourceNom.Items.Add(CreateBigButton("Export", "XTS_NomenclatureExport", Utils.IconType.Export, "Export Nomencl."));
            sourceNom.Items.Add(CreateBigButton("Import", "XTS_NomenclatureImport", Utils.IconType.Import, "Import Nomencl."));
            sourceNom.Items.Add(CreateBigButton("Sync", "XTS_NomenclatureSync", Utils.IconType.Sync, "Sync Nomencl."));

            // 6. TOOLS
            var sourceTools = new RibbonPanelSource { Title = "Tools" };
            var panelTools = new RibbonPanel { Source = sourceTools };
            tab.Panels.Add(panelTools);
            sourceTools.Items.Add(CreateBigButton("Copy Params", "XTS_CopyAllParams", Utils.IconType.Copy, "Copy All Params."));
            sourceTools.Items.Add(CreateBigButton("Find Handle", "XTS_SelectByHandle", Utils.IconType.Search, "Find Handle."));
            sourceTools.Items.Add(new RibbonSeparator());
            sourceTools.Items.Add(CreateBigButton("Select Info", "XTS_SelectByProperty", Utils.IconType.Filter, "Select By Prop."));
            sourceTools.Items.Add(CreateBigButton("Select Clean", "XTS_SelectClean", Utils.IconType.Empty, "Select Clean."));
            sourceTools.Items.Add(CreateBigButton("Duplicates", "XTS_SelectDuplicates", Utils.IconType.Duplicate, "Select Dupl."));
            sourceTools.Items.Add(CreateBigButton("Audit Cloud", "XTS_AuditModel", Utils.IconType.Database, "Audit Model."));
            sourceTools.Items.Add(CreateBigButton("Colorize", "XTS_ColorizeByStatus", Utils.IconType.Import, "Colorize."));

            // 6b. NETWORK TOOLS — Material + Apply Rules
            var sourcePipe = new RibbonPanelSource { Title = "Network Tools" };
            var panelPipe = new RibbonPanel { Source = sourcePipe };
            tab.Panels.Add(panelPipe);
            sourcePipe.Items.Add(CreateBigButton("Material\nEstructuras", "XTS_SetMaterialStructures", Utils.IconType.Material, "Cambia el material a todas las estructuras seleccionadas. Selecciona primero, luego haz clic."));
            sourcePipe.Items.Add(CreateBigButton("Material\nTuberías", "XTS_SetMaterialPipes", Utils.IconType.Material, "Cambia el material a todas las tuberías seleccionadas. Selecciona primero, luego haz clic."));
            sourcePipe.Items.Add(new RibbonSeparator());
            sourcePipe.Items.Add(CreateBigButton("Apply Rules\nTuberías", "XTS_ApplyRulesPipes", Utils.IconType.Pipe, "Aplica reglas de diseño a todas las tuberías de las redes del dibujo."));
            sourcePipe.Items.Add(CreateBigButton("Apply Rules\nEstructuras", "XTS_ApplyRulesStructures", Utils.IconType.Junction, "Aplica reglas de diseño a todas las estructuras de las redes del dibujo."));

            // 7. INFO
            var sourceVer = new RibbonPanelSource { Title = "Info" };
            var panelVer = new RibbonPanel { Source = sourceVer };
            tab.Panels.Add(panelVer);
            sourceVer.Items.Add(CreateBigButton($"Version: {Version}\nUpdated: {LastUpdate}", "XTS_INFO", Utils.IconType.XTS, $"Hora de carga: {LastUpdate}"));
            
            isRibbonCreated = true;
        }

        private RibbonButton CreateBigButton(string text, string cmd, Utils.IconType icon, string tooltip)
        {
            var btn = new RibbonButton
            {
                Text = text,
                CommandParameter = cmd + " ",
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                CommandHandler = new RibbonCommandHandler(),
                ToolTip = new RibbonToolTip { Title = text, Content = tooltip }
            };
            btn.Image = Utils.GetVectorIcon(icon);
            btn.LargeImage = Utils.GetVectorIcon(icon);
            return btn;
        }
    }

    public class RibbonCommandHandler : System.Windows.Input.ICommand
    {
        public bool CanExecute(object parameter) => true;
        public event EventHandler CanExecuteChanged;
        public void Execute(object parameter)
        {
            try {
                if (parameter is RibbonButton btn)
                    AutoCADApp.DocumentManager.MdiActiveDocument.SendStringToExecute((string)btn.CommandParameter, true, false, true);
            } catch { }
        }
    }
}
