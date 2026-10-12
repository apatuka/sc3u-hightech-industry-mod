// SC3U High-Tech Industry mod - SimCity 3000 Unlimited (GOG):
//   a 4th industrial zone "High-Tech Industrial" (only clean / high-tech industry is built there),
//   high-tech industry and zones shown in magenta on Map View > Zones,
//   Query: "(High-Tech)" building names and a city-wide "High-Tech industries in city: N" line.
using System;
using System.Reflection;

[assembly: AssemblyTitle("SC3U High-Tech Industry")]
[assembly: AssemblyDescription("SimCity 3000 Unlimited: High-Tech industrial zone, map colour and Query information")]
[assembly: AssemblyProduct("SC3U High-Tech Industry")]
[assembly: AssemblyCopyright("Community mod - MIT License - not affiliated with EA or Maxis")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace SC3UMod
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            ModInfo m = new ModInfo();
            m.Title = "SC3U High-Tech Industry";
            m.Version = "v1.0";
            m.BackupFolder = @"SC3U_Mod_Backups\HighTech";
            m.DescriptionEs =
                "Añade todo lo relacionado con la industria High-Tech (industria limpia):\n" +
                "  • Zonificación → Industrial → «High-Tech Industrial»: una 4ª zona donde solo se\n" +
                "    construye industria High-Tech, en las tres densidades;\n" +
                "  • Map View → Zones: las zonas y los edificios High-Tech se ven en magenta;\n" +
                "  • Query: «(High-Tech)» en el nombre del edificio y el total de industrias High-Tech de la ciudad.\n\n" +
                "Compatible con SimCity 3000 Unlimited (versión GOG) y con SC3U Full RCI Patch.\n" +
                "Las ciudades que usen la zona High-Tech necesitan este mod instalado.";
            m.DescriptionEn =
                "Adds everything about High-Tech (clean) industry:\n" +
                "  • Zoning → Industrial → \"High-Tech Industrial\": a 4th zone where only High-Tech\n" +
                "    industry is built, in all three densities;\n" +
                "  • Map View → Zones: High-Tech zones and buildings are shown in magenta;\n" +
                "  • Query: \"(High-Tech)\" in the building name and the city's total of High-Tech industries.\n\n" +
                "Works with SimCity 3000 Unlimited (GOG version) and with SC3U Full RCI Patch.\n" +
                "Cities that use the High-Tech zone need this mod installed.";
            m.Components.Add(new DllComponent(PatchData.SimRci,
                "SIMRCI.DLL: zona High-Tech (tipo 12) y color magenta en el mapa",
                "SIMRCI.DLL: High-Tech zone (type 12) and magenta map colour"));
            m.Components.Add(new DllComponent(PatchData.SimGeom,
                "SIMGEOM.DLL: total de industrias High-Tech en el Query",
                "SIMGEOM.DLL: total of High-Tech industries in the Query"));
            m.Components.Add(new ZoneDataComponent());
            m.Components.Add(new ZoneStringsComponent());
            m.Components.Add(new BuildingNamesComponent());
            m.DllShaOriginal[PatchData.SimRci.FileName] = PatchData.SimRci.ShaOriginal;
            m.DllShaOriginal[PatchData.SimGeom.FileName] = PatchData.SimGeom.ShaOriginal;
            return App.Start(m, args);
        }
    }
}
