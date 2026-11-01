using RPGMDataKit.Storages;
using RPGMDataKit.Storages.Abstraction;

namespace RPGMDataSearch;

/// <summary>Opens a project by its folder: MV (the folder with "data", or the project folder with "www") or VX Ace (the folder with "Data\*.rvdata2").</summary>
internal static class ProjectLoader
{
    public static (Storage Storage, string Description) Open(string folder)
    {
        folder = folder.Trim().Trim('"');
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"There is no folder {folder}");

        if (File.Exists(Path.Combine(folder, "data", "System.json")))
            return (new MVStorage(folder), "MV: " + folder);

        if (File.Exists(Path.Combine(folder, "www", "data", "System.json")))
        {
            var www = Path.Combine(folder, "www");
            return (new MVStorage(www), "MV: " + www);
        }

        foreach (var name in new[] { "Data", "data" })
        {
            if (File.Exists(Path.Combine(folder, name, "System.rvdata2")))
                return (new VXAceStorage(folder, name), "VX Ace: " + folder);
        }

        throw new FileNotFoundException("It is not a project of MV (data\\System.json) or VX Ace (Data\\System.rvdata2)");
    }
}
