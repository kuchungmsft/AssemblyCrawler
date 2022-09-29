using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace AssemblyCrawler
{
    internal class Crawler
    {
        public string ParsePath { get; private set; }
        public int TotalAssemblyCount { get; private set; }
        public int TotalFileCount { get; private set; }
        public int TotalNonResourceAssemblyCount { get; private set; }
        public string DevenvVersion { get; private set; }
        public IReadOnlyDictionary<string, List<AssemblyInfo>> AllAssemblies { get { return sortedAssemblies_all.ToImmutableDictionary(); } }
        public IReadOnlyDictionary<string, List<AssemblyInfo>> AllManagedAssemblies { get { return sortedAssemblies_managed.ToImmutableDictionary(); } }
        public IReadOnlyDictionary<string, List<AssemblyInfo>> AllNonSortedAssemblies { get { return nonsortedAssemblies_all.ToImmutableDictionary(); } }
        public IReadOnlyDictionary<string, List<AssemblyInfo>> AllNonSortedManagedAssemblies { get { return nonsortedAssemblies_managed.ToImmutableDictionary(); } }


        public List<AssemblyInfo> AssemblyList { get => assemblies.ToList(); }
        private readonly List<AssemblyInfo> assemblies = new List<AssemblyInfo>();
        private Dictionary<string, List<AssemblyInfo>> sortedAssemblies_all = new Dictionary<string, List<AssemblyInfo>>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, List<AssemblyInfo>> sortedAssemblies_managed = new Dictionary<string, List<AssemblyInfo>>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, List<AssemblyInfo>> nonsortedAssemblies_all = new Dictionary<string, List<AssemblyInfo>>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, List<AssemblyInfo>> nonsortedAssemblies_managed = new Dictionary<string, List<AssemblyInfo>>(StringComparer.OrdinalIgnoreCase);

        public Crawler(string path)
        {
            this.ParsePath = path;
            TotalAssemblyCount = 0;
            TotalFileCount = 0;
        }

        public void Crawl()
        {
            if (assemblies.Any())
            {
                Console.WriteLine($"Crawler has already crawled directory '{ParsePath}'.");
                return;
            }

            DirectoryInfo d = new DirectoryInfo(ParsePath);
            DevenvVersion = "N/A";
            Stopwatch sw = new Stopwatch();
            sw.Start();
            CrawlDirectory(d);
            sw.Stop();

            Console.WriteLine($"Crawled '{ParsePath}' in {sw.ElapsedMilliseconds}ms.");
        }

        public void CrawlDirectory(DirectoryInfo d)
        {
            var files = d.EnumerateFiles().ToList();
            var directories = d.EnumerateDirectories().ToList();

            var assemblyFiles = files.Where(file => string.Equals(file.Extension, ".dll", StringComparison.OrdinalIgnoreCase)
            && !file.Attributes.HasFlag(FileAttributes.ReparsePoint)); // Ignore symlinks

            TotalFileCount += files.Count();
            TotalAssemblyCount += assemblyFiles.Count();
            TotalNonResourceAssemblyCount += assemblyFiles.Where(x => !x.IsResource()).Count();

            var devenv = files.FirstOrDefault(x => string.Equals(x.Name, "devenv.exe", StringComparison.OrdinalIgnoreCase));
            if (devenv is not null)
            {
                DevenvVersion = FileVersionInfo.GetVersionInfo(devenv.FullName).ProductVersion!;
            }

            assemblies.AddRange(assemblyFiles.Select(a => new AssemblyInfo(a)));

            if (directories.Any())
            {
                foreach (var directory in directories)
                {
                    CrawlDirectory(directory);
                }
            }
        }

        public void Sort()
        {
            if (!AssemblyList.Any())
            {
                Console.WriteLine("Must parse directory first.");
                return;
            }

            var assemblies = this.assemblies.ToList();

            Console.WriteLine($"Found in `{ParsePath}`:");
            Console.WriteLine($"    Total Files:      {TotalFileCount}");
            Console.WriteLine($"    Total Assemblies: {TotalAssemblyCount}");
            Console.WriteLine($"    Total Non Resource Assemblies:      {TotalNonResourceAssemblyCount}");
            Console.WriteLine($"    Devenv Version:      {DevenvVersion}");

            Console.WriteLine($"Starting sort.");
            Stopwatch sw = new Stopwatch();
            sw.Start();

            foreach (var a in assemblies)
            {
                if (!sortedAssemblies_all.ContainsKey(a.FName.Value))
                {
                    sortedAssemblies_all.Add(a.FName.Value, new List<AssemblyInfo>());
                }

                sortedAssemblies_all[a.FName.Value].Add(a);
            }

            nonsortedAssemblies_all = new Dictionary<string, List<AssemblyInfo>>(sortedAssemblies_all);

            var keyList = sortedAssemblies_all.Keys.ToList();
            foreach (var key in keyList)
            {
                if (sortedAssemblies_all[key].Count() == 1)
                {
                    sortedAssemblies_all.Remove(key);
                }
            }

            sortedAssemblies_managed = new Dictionary<string, List<AssemblyInfo>>(sortedAssemblies_all);
            nonsortedAssemblies_managed = new Dictionary<string, List<AssemblyInfo>>(nonsortedAssemblies_all);

            keyList = sortedAssemblies_managed.Keys.ToList();
            foreach (var key in keyList)
            {
                if (sortedAssemblies_managed[key][0].IsManaged.Value == false)
                {
                    sortedAssemblies_managed.Remove(key);
                }

                if (nonsortedAssemblies_managed[key][0].IsManaged.Value == false)
                {
                    nonsortedAssemblies_managed.Remove(key);
                }
            }

            Console.WriteLine($"Sort complete in {sw.ElapsedMilliseconds}ms");
        }

        private const string AssemblyCacheFolderName = "AssemblyCache";
        private const string PreCacheFileName = "files.txt";
        public void CreateSymlinks(string assemblyName)
        {
            //copy each specific assembly
            if (string.IsNullOrWhiteSpace(assemblyName) || !sortedAssemblies_managed.ContainsKey(assemblyName))
            {
                Console.WriteLine("Empty or invalid assembly name");
            }

            var list = sortedAssemblies_managed[assemblyName];
            if (!list.Any())
            {
                Console.WriteLine($"No duplicates found for ${assemblyName}.");
                return;
            }

            var assemblyByHashCode = list.SortByHashCode();

            // create new folder
            var vsInstallationFolder = System.IO.Path.Combine(ParsePath, "Common7", "IDE");
            while (!Directory.Exists(vsInstallationFolder))
            {
                Console.WriteLine($"Installation not found at '{vsInstallationFolder}'");
                Console.WriteLine("Enter location of Visual Studio Installation:");
                vsInstallationFolder = Console.ReadLine();

                if (String.IsNullOrWhiteSpace(vsInstallationFolder))
                {
                    return;
                }
            }

            var assemblyCacheFolder = Path.Combine(vsInstallationFolder, AssemblyCacheFolderName);
            CreateDirectoryIfNotExists(assemblyCacheFolder);

            var assemblyNameFolder = Path.Combine(assemblyCacheFolder, assemblyName);
            CreateDirectoryIfNotExists(assemblyNameFolder);

            var keys = assemblyByHashCode.Keys.ToList();
            //create symlinks            
            foreach (var key in keys)
            {
                try
                {
                    var hashCodeFolder = Path.Combine(assemblyNameFolder, key.ToString("X8"));
                    CreateDirectoryIfNotExists(hashCodeFolder);

                    var instances = assemblyByHashCode[key].Where(item => !Path.GetFullPath(item.Path).StartsWith(Path.GetFullPath(hashCodeFolder), StringComparison.OrdinalIgnoreCase)).ToList();

                    if (!instances.Any())
                    {
                        continue;
                    }

                    var paths = instances.Select(item => item.Path).ToList();

                    var precacheFileName = Path.Combine(hashCodeFolder, PreCacheFileName);

                    {
                        using FileStream fs = new FileStream(precacheFileName, FileMode.Append);
                        using StreamWriter sw = new StreamWriter(fs);

                        //don't pick up the ones in the cache folder
                        paths.ForEach(item => sw.WriteLine(item));
                        sw.Flush();
                        sw.Close();
                    }

                    var fileName = instances[0].FName.Value;
                    var cacheAssemblyFileName = Path.Combine(hashCodeFolder, assemblyName);

                    // copy one into it if it doesn't exist
                    if (!File.Exists(cacheAssemblyFileName))
                    {
                        //these should all be the same so we don't care which one we're copying
                        File.Copy(Path.Combine(paths[0], fileName), cacheAssemblyFileName);
                    }

                    foreach (var path in paths)
                    {
                        var target = Path.Combine(path, fileName);
                        try
                        {
                            File.Delete(target);
                            MakeSymLink(target, cacheAssemblyFileName);
                        }
                        catch
                        {
                            //undo the delete if soemthing bad happens
                            if (!File.Exists(target))
                            {
                                File.Copy(cacheAssemblyFileName, target);
                            }
                            throw;
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    Console.WriteLine("Don't have access");
                    throw;
                }
            }
        }

        private static void CreateDirectoryIfNotExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private static void MakeSymLink(string symLinkPath, string destinationFile)
        {
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", $"/c mklink \"{symLinkPath}\" \"{destinationFile}\"");
            var process = Process.Start(psi);
            while (!process.HasExited)
            {
                process.WaitForExit();
            }

            if (process.ExitCode != 0)
            {
                Console.WriteLine($"Failed to create symlink {symLinkPath} pointing to {destinationFile}. ExitCode {process.ExitCode}.");
                throw new InvalidOperationException();
            }
        }
    }
}
