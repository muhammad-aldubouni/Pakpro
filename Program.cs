
string s = "";
string Package = "";
string Option = "";
string Container = "";
string Path = (AppDomain.CurrentDomain.BaseDirectory + "/packages/").Replace("//", "/");
string BasePath = AppDomain.CurrentDomain.BaseDirectory.Replace("//", "/");
string HomePath = ((await ExecuteCommand("echo ~/", false)).Replace(Environment.NewLine, "") + "/").Replace("//", "/");

try
{
    Container = System.IO.File.ReadAllText($"{BasePath}container").Replace(Environment.NewLine, "").Trim();
}
catch
{
    Console.WriteLine($"\n\nno container specified . \n\n");
    Environment.Exit(1);
}



List<string> Exceptions = new() { "clean", "setup" };


try
{
    s = args[0];
}
catch { }

if (!Exceptions.Contains(s))
{
    try
    {
        string output = await ExecuteCommand($"sudo docker exec {Container} ls", false);
        if (output.Contains("is not running") || output == "")
        {
            Console.WriteLine($"\n\nstarting {Container} . \n\n");
            await ExecuteCommand($"sudo docker start {Container}", true);
            Console.WriteLine($"\n\n{Container} started . \n\n");
        }
    }
    catch
    {

        Console.WriteLine($"\n\nhaving some issues with {Container} . \n\n");
    }

}


switch (s)
{

    case "setup":
        {

            string ImagePath = "";
            Console.WriteLine("\n\nsetting up pakpro : \n\n");
            Package = "docker-ce";
            await Install(true);

            try
            {
                ImagePath = args[1];
            }
            catch
            {
                Console.WriteLine($"\n\nnote : you need to create and manage \"{Container}\" manually");
                Environment.Exit(1);
            }

            await ExecuteCommand($"sudo docker load -i {ImagePath}", true);
            await ExecuteCommand($"sudo docker create -it --name {Container} pakster", true);
            string output = await ExecuteCommand($"sudo docker start {Container}", true);


            if (output.Contains("Error:") || output == "")
                Console.WriteLine($"\n\nnote : you need to create and manage \"{Container}\" manually");


            Package = "deborphan";
            await Install(true);

            break;
        }

    case "download":
        {
            await GetPackageName();
            await Download(await GetDpendencies(), false);
            break;
        }

    case "resume-download":
        {
            await GetPackageName();
            await Download(await GetDpendencies(), true);
            break;
        }

    case "clean":
        {
            await Clean();
            break;
        }

    case "install":
        {
            GetOption();
            await GetPackageName();
            if (Option == "yes") await Install(true);
            else await Install(false);
            break;
        }

    case "update":
        {
            await Update();
            break;
        }

    case "upgrade":
        {
            await Upgrade();
            break;
        }

    case "rm-pak":
        {
            await GetPackageName();
            await Remove();
            break;
        }

    case "clean-kept-paks":
        {
            await CleanKeptPackages();
            break;
        }

    case "purge":
        {
            await GetPackageName();
            await Purge();
            break;
        }

    default:
        {
            Console.WriteLine("\n\nunknown option . \n\n");
            break;
        }
}


async Task<string> ExecuteCommand(string cmd, bool stream)
{
    var process = new System.Diagnostics.Process();

    process.StartInfo.FileName = "bash";
    process.StartInfo.Arguments = $"-c \"{cmd}\"";
    process.StartInfo.UseShellExecute = false;
    process.StartInfo.RedirectStandardOutput = true;

    process.Start();
    string output = "";
    using (StreamReader reader = process.StandardOutput)
    {
        while (!reader.EndOfStream)
        {
            string? realTimeOutput = await reader.ReadLineAsync();

            // Process the output in real-time
            if (stream) Console.WriteLine(realTimeOutput);
            output += realTimeOutput + Environment.NewLine;
        }
    }

    process.WaitForExit();


    //Console.WriteLine(output);
    return output;
}


async Task GetPackageName()
{
    try
    {
        Package = args[1];
    }
    catch
    {
        Console.WriteLine("\n\nno package name provided . \n\n");
        Environment.Exit(1);
    }
    //sudo docker exec {Container} 
    string output = await ExecuteCommand($"sudo docker exec {Container} apt-cache policy {Package}", false);
    if (output.Contains("Unable to locate package") || output == "" /*|| output.Contains("Candidate: (none)")*/)
    {
        Console.WriteLine("\n\npackage not found . \n\n");
        Environment.Exit(1);
    }

}


void GetOption()
{
    try
    {
        Option = args[2];
    }
    catch { }
}

async Task<List<string>> GetDpendencies()
{
    string output = await ExecuteCommand($"sudo docker exec {Container} apt-get --print-uris install {Package} | awk " + "'{print $1}'", false);

    List<string> DependenciesLinks = new();
    DependenciesLinks = output.Split(Environment.NewLine).ToList();

    for (int i = 0; i < DependenciesLinks.Count; i++)
    {
        if (DependenciesLinks[i].Contains("http://") || DependenciesLinks[i].Contains("https://") || DependenciesLinks[i].Replace("'", "").EndsWith(".deb"))
        {
            DependenciesLinks[i] = DependenciesLinks[i].Replace("'", "");
        }
        else
        {
            DependenciesLinks.RemoveAt(i);
            i--;
        }
    }

    return DependenciesLinks;
}


async Task<List<string>> GetInstallInstructions(bool forThisMachine, bool fixInst, bool viewPaks)
{

    string output = "";
    if (forThisMachine && (!fixInst))
        output = await ExecuteCommand($"sudo apt-get --simulate install {Package}", false);
    if ((!forThisMachine) && (!fixInst))
        output = await ExecuteCommand($"sudo docker exec {Container} apt-get --simulate install {Package}", false);


    // used to get and detect packages need to be fixed
    if (fixInst) output = await ExecuteCommand("apt-get --simulate install -f", false);



    List<string> instructions = output.Split(Environment.NewLine).ToList();
    for (int i = 0; i < instructions.Count; i++)
    {
        if (!instructions[i].StartsWith("Inst") && !instructions[i].StartsWith("Conf"))
        {
            instructions.RemoveAt(i);
            i--;
        }
        else if (instructions[i].StartsWith("Conf")) instructions[i] = "[Conf]";
        else instructions[i] = instructions[i].Replace("Inst ", "").Split(" ")[0];
    }


    // make sectors of installiation seperated of conf
    bool triger = false;
    for (int i = 0; i < instructions.Count; i++)
    {
        if (instructions[i].StartsWith("[Conf]"))
        {
            if (triger == false)
            {
                triger = true;
                continue;
            }
            else
            {
                instructions.RemoveAt(i);
                i -= 1;
            }
        }
        else triger = false;
    }

    if (fixInst) return instructions;

    List<string> packagesToInstall = new();
    string paks = "";
    foreach (string inst in instructions)
    {
        string pak = "";
        if (inst != "[Conf]")
        {
            List<string> DebPaks = (await ExecuteCommand($"ls {Path}{Package}/ | grep {inst}_", false)).Split(Environment.NewLine).ToList();
            foreach (string debPak in DebPaks) if (debPak.StartsWith(inst + "_")) { pak = debPak.Replace(Environment.NewLine, ""); break; }
            if (pak == "")
            {
                Console.WriteLine($"\n\ncouldn't find deb files of {inst} . \n\n");
                Environment.Exit(1);
            }
            paks += $"./{pak} ";
        }
        else
        {
            packagesToInstall.Add(paks);
            paks = "";
        }

    }
    if (paks != "") packagesToInstall.Add(paks);
    if (viewPaks)
    {
        foreach (string group in packagesToInstall) Console.WriteLine(group.Replace("./", Environment.NewLine));
    }

    return packagesToInstall;
}


async Task CleanSetupFiles()
{
    Console.Write("\n\ncleaning setup files");
    if (System.IO.Directory.GetFiles($"{HomePath}files/system-management/", "*.setup").Length != 0)
    {
        await ExecuteCommand($"rm {HomePath}files/system-management/*.setup", true);
        Console.WriteLine(" ---> [  ✓  ] \n\n");
    }
    else Console.WriteLine(" ---> [ clean ] \n\n");
}


async Task Clean()
{
    await CleanSetupFiles();

}


async Task Download(List<string> DependenciesLinks, bool resume)
{
    if (DependenciesLinks.Count == 0)
    {
        Console.WriteLine("\n\nthere is no packages to download . \n\n");
        Environment.Exit(1);
    }

    string links = "";
    foreach (string link in DependenciesLinks) links += link + Environment.NewLine;
    System.IO.File.WriteAllText($"{BasePath}tmp/links", links);
    Console.WriteLine(Environment.NewLine);
    if (resume)
        await ExecuteCommand($"cd {Path}{Package} && wget -c -q --show-progress -i {BasePath}tmp/links", true);
    else
        await ExecuteCommand($"mkdir {Path}{Package} && cd {Path}{Package} && wget -q --show-progress -i {BasePath}tmp/links", true);

    if (System.IO.Directory.GetFiles($"{Path}{Package}").Length == 0)
    {
        Console.WriteLine("\n\nsomething went wrong, please check your connection . \n\n");
        await ExecuteCommand($"sudo rm -r {Path}{Package}", true);
        Environment.Exit(1);
    }
    //await GetIndex();
    Console.WriteLine("\n\nwriting data please wait . \n\n");
    List<string> Instructions = await GetInstallInstructions(false, false, false);
    string InstallGuid = "";
    foreach (string instruction in Instructions) InstallGuid += $"{instruction}" + Environment.NewLine;
    System.IO.File.WriteAllText($"{Path}{Package}/install.guid", InstallGuid);
    //await ExecuteCommand($"rm {BasePath}tmp/links", true);
    Console.WriteLine($"\n\ndownloading {Package} ---> [  ✓  ] \n\n");
}


async Task Install(bool isApproved)
{
    if (System.IO.Directory.Exists($"{Path}{Package}") && System.IO.Directory.GetFiles($"{Path}{Package}/").Length >= 2)
    {
        List<string> Instructions = new();
        List<List<string>> Sectors = new();
        List<string> PaksToInstall = new();


        try
        {
            Instructions = System.IO.File.ReadAllText($"{Path}{Package}/install.guid").Split(Environment.NewLine).ToList();
        }
        catch
        {
            Console.WriteLine($"\n\ncan't find any guid to install {Package} . \n\n");
            Environment.Exit(1);

        }



        string PakState = await ExecuteCommand($"sudo apt-cache policy {Package}", false);
        bool removePak = ((!PakState.Contains("Installed: (none)")) && PakState != "" && PakState != Environment.NewLine);
        if (removePak)
        {
            Console.WriteLine($"\n\nlooks like {Package} already installed . \n\n");
            Environment.Exit(1);
        }




        Console.WriteLine("\n\ndetermining needed packages ---> [working on it] \n\n");

        for (int i = 0; i < Instructions.Count; i++)
        {
            List<string> Paks = Instructions[i].Split("./").ToList();
            for (int j = 0; j < Paks.Count; j++)
            {
                if (Paks[j] == "" || Paks[j] == " ")
                {
                    Paks.RemoveAt(j);
                    j--;
                    continue;
                }
                string output = await ExecuteCommand("sudo apt-cache policy " + Paks[j].Remove(Paks[j].IndexOf("_")), false);
                removePak = ((!output.Contains("Installed: (none)")) && output != "" && output != Environment.NewLine);
                if (removePak)
                {
                    Paks.RemoveAt(j);
                    j--;
                }

            }

            if (Paks.Count != 0) Sectors.Add(Paks);



        }


        if (!System.IO.Directory.Exists($"{HomePath}.pakpro"))
            await ExecuteCommand($"mkdir {HomePath}.pakpro", true);

        if (System.IO.File.Exists($"{HomePath}.pakpro/{Package}.inst"))
            await ExecuteCommand($"rm {HomePath}.pakpro/{Package}.inst", false);

        Console.WriteLine("\n\ninstalling the following packages : \n\n");
        foreach (List<string> Sector in Sectors)
        {
            string Paks = "";
            foreach (string Pak in Sector)
            {
                Paks += $"./{Pak} ";
                Console.WriteLine(Pak);
            }
            System.IO.File.AppendAllLines($"{HomePath}.pakpro/{Package}.inst", Sector);
            PaksToInstall.Add(Paks);
            Console.WriteLine("\n\n");

        }

        Console.WriteLine("\n\ndetermining needed packages ---> [  ✓  ] \n\n");



        string SetupScript = $"cd {Path}{Package}";
        foreach (string Sector in PaksToInstall)
        {
            SetupScript += Environment.NewLine + $"sudo dpkg --force-depends-version -i {Sector}";
            SetupScript += Environment.NewLine + $"sudo dpkg --force-depends-version --configure -a";
        }
        System.IO.File.WriteAllText($"{HomePath}files/system-management/{Package}.setup", SetupScript);
        await ExecuteCommand($"sudo chmod +x {HomePath}files/system-management/{Package}.setup", true);



        string? input = "";

        if (!isApproved)
        {
            Console.Write($"\n\ncontinue [yes] ? :   ");
            input = Console.ReadLine();
            Console.WriteLine(Environment.NewLine);
        }
        if (input == "yes" || isApproved)
        {
            //await CleanIndex();
            await ExecuteCommand($"{HomePath}files/system-management/{Package}.setup", true);
            Console.WriteLine($"\n\ninstalling {Package} ---> [  ✓  ] \n\n");
            Console.WriteLine(Environment.NewLine);
            await CleanSetupFiles();
        }

        else
        {
            Console.WriteLine($"\n\n{Package} installation wasn't approved . \n\n");
            //await CleanIndex();
            await CleanSetupFiles();
            Environment.Exit(1);
        }


    }
    else
    {
        await Download(await GetDpendencies(), false);
        await Install(false);
    }
}


async Task<bool> Remove()
{
    Console.WriteLine("\n");
    await ExecuteCommand($"sudo rm -r {Path}{Package}", true);
    Console.WriteLine($"\n\n{Package} reomved . \n\n");
    return true;
}



async Task<bool> Purge()
{
    try
    {

        if (System.IO.File.Exists($"{HomePath}.pakpro/{Package}.inst"))
        {

            List<string> Paks = System.IO.File.ReadAllText($"{HomePath}.pakpro/{Package}.inst").Split(Environment.NewLine).ToList();
            for (int i = 0; i < Paks.Count; i++)
            {
                if (Paks[i] == "" || Paks[i] == " " || Paks[i] == "  ")
                {
                    Paks.RemoveAt(i);
                    i--;
                    continue;
                }
                Paks[i] = Paks[i].Remove(Paks[i].IndexOf("_"));
            }
            Paks.Reverse();
            Console.WriteLine(Environment.NewLine);
            foreach (string pak in Paks) Console.WriteLine(pak);
            Console.WriteLine(Environment.NewLine);

            List<string> PaksDependsOn = new();
            List<string> PaksToPurge = new();
            List<string> KptDueToPaksHistory = new();


            string PackagesKept = "";
            Dictionary<string, List<string>> OverAllKptPaks = new();
            foreach (string pak in Paks) OverAllKptPaks.Add(pak, new List<string>());


            Console.WriteLine("\n\ndetermining dependencies .... \n\n");
            for (int i = 0; i < Paks.Count; i++)
            {
                string output = (await ExecuteCommand("sudo deborphan " + Paks[i], false)).Replace(" ", "").Replace("\t", "").Replace("|", "");
                PaksDependsOn = output.Split(Environment.NewLine).ToList(); if (PaksDependsOn.Contains(Environment.NewLine) || PaksDependsOn.Contains("")) { PaksDependsOn.Remove(Environment.NewLine); PaksDependsOn.Remove(""); }
                PaksDependsOn.Remove(Paks[i]);
                bool keep = false;
                List<string> KptDueToPaks = new();

                foreach (string pak in PaksDependsOn)
                {

                    if ((!Paks.Contains(pak) || KptDueToPaksHistory.Contains(pak)) && Paks[i] != Package)
                    {
                        KptDueToPaks.Add(pak);
                        KptDueToPaksHistory.Add(Paks[i]);
                        KptDueToPaksHistory = KptDueToPaksHistory.Distinct().ToList();
                        keep = true;
                    }
                }

                if (!keep)
                {
                    if (!PaksToPurge.Contains(Paks[i])) PaksToPurge.Add(Paks[i]);
                }
                else
                {
                    string PakToKeep = Paks[i];
                    foreach (string pak in KptDueToPaks)
                    {
                        if (!OverAllKptPaks[PakToKeep].Contains(pak))
                        {
                            OverAllKptPaks[PakToKeep].Add(pak);
                            PaksToPurge.Remove(PakToKeep);
                            i = -1;
                        }
                    }
                }
            }


            foreach (string pak in OverAllKptPaks.Keys)
            {
                if (OverAllKptPaks[pak].Count != 0)
                {
                    System.IO.File.WriteAllText($"{HomePath}.pakpro/{pak}.kept", Package);
                    PackagesKept += Environment.NewLine + Environment.NewLine + pak + " needed by : ";
                }

                foreach (string kptPak in OverAllKptPaks[pak])
                {
                    PackagesKept += kptPak + "  ";
                }
            }



            Console.WriteLine("\n\npurging the following packages : \n\n");
            foreach (string pak in PaksToPurge) Console.WriteLine(pak);
            if (PackagesKept != "")
            {
                Console.WriteLine("\n\n\n\n\nkeeping the following packages due to some dependencies : ");
                Console.WriteLine($"\n\n{PackagesKept} \n\n");
            }
            Console.Write($"\n\ncontinue [yes] ? :   ");
            string? input = Console.ReadLine();
            Console.WriteLine(Environment.NewLine);
            if (input == "yes")
            {
                foreach (string pak in PaksToPurge) await ExecuteCommand($"sudo dpkg --force-depends --purge {pak}", true);
                await ExecuteCommand($"sudo apt-get -y autoremove && sudo rm {HomePath}.pakpro/{Package}.inst", true);
                Console.WriteLine($"\n\npurging {Package} ---> [  ✓  ] \n\n");
            }

            else
            {
                Console.WriteLine($"\n\npurging wasn't approved . \n\n");
                Environment.Exit(1);
            }

        }
        else
        {
            Console.WriteLine("\n\ncan't purge this package since it is not installed or it is installed by another package manager . \n\n");
            Environment.Exit(1);
        }
    }
    catch (Exception e)
    {
        Console.WriteLine(e.Message);
    }

    return true;
}


async Task<bool> CleanKeptPackages()
{

    try
    {


        List<string> KptFiles = System.IO.Directory.GetFiles($"{HomePath}.pakpro/", "*.kept").ToList();

        Dictionary<string, string> DebLoc = new();

        foreach (string pak in KptFiles)
        {

            string PakName = pak.Substring(pak.LastIndexOf("/") + 1).Replace(".kept", "");
            DebLoc.Add(PakName, System.IO.File.ReadAllText(pak).Replace(Environment.NewLine, ""));
            await ExecuteCommand($"sudo dpkg --force-depends --purge {PakName}", true);
            await ExecuteCommand($"rm {pak}", true);


        }


        List<string> PaksToInstall = await GetInstallInstructions(true, true, true);

        foreach (string pak in PaksToInstall)
        {

            string debFile = "";
            try
            {

                if (pak != "[Conf]") debFile = System.IO.Directory.GetFiles(Path + DebLoc[pak], $"{pak}*")[0];
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
            if (pak == "[Conf]") await ExecuteCommand("sudo dpkg --configure -a", true);
            else
            {
                await ExecuteCommand($"sudo dpkg -i {debFile}", true);
                System.IO.File.WriteAllText($"{HomePath}.pakpro/{pak}.kept", DebLoc[pak]);
            }
        }


    }
    catch (Exception e)
    {
        Console.WriteLine(e.Message);
    }
    return true;
}


async Task<bool> Update()
{
    Console.WriteLine("\n\nupdating ---> [working on it]\n\n");
    await ExecuteCommand($"sudo docker exec {Container} apt-get update", true);
    Console.WriteLine("\n\nupdating ---> [  ✓  ]\n\n");
    return true;
}


async Task<bool> Upgrade()
{
    Console.WriteLine("\n\nupgrading ---> [working on it]\n\n");
    await ExecuteCommand("sudo apt-get update", true);
    await ExecuteCommand("sudo apt-get upgrade -y", true);
    Console.WriteLine("\n\nupgrading ---> [  ✓  ]\n\n");
    return true;
}
