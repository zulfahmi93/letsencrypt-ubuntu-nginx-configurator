using Microsoft.Extensions.PlatformAbstractions;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using static System.Console;


namespace LetsEncryptNginxConfigurator
{
    public class Program
    {
        #region Constants

        private const string NginxDirectory = "/etc/nginx/";
        private const string SitesAvailableDirectory = NginxDirectory + "sites-available/";
        private const string NginxSnippetsDirectory = NginxDirectory + "snippets/";
        private const string CronDirectory = "/etc/cron.d/";

        private const string SitesAvailableDefaultConfigPath = SitesAvailableDirectory + "default";
        private const string SslDomainSnippetConfigPathFormat = NginxSnippetsDirectory + "ssl-{0}.conf";
        private const string SslParamsSnippetConfigPath = NginxSnippetsDirectory + "ssl-params.conf";
        private const string LetsEncryptCronFilePath = CronDirectory + "letsencrypt";

        private static readonly char[] SplitChars = { ' ', '-', '\t' };

        #endregion


        #region Fields

        private static string _homePath;
        private static string _appPath;
        private static int _count;

        #endregion

        public static int Main()
        {
            _homePath = Environment.GetEnvironmentVariable("HOME");
            _appPath = PlatformServices.Default.Application.ApplicationBasePath;

            if (string.IsNullOrWhiteSpace(_homePath))
            {
                PrintWarning("Unable to retrieve your HOME folder location as HOME environment was not defined!");
                return 1;
            }

            PrintWarning("Your existing nginx configuration will be replaced! The program will make a backup of your current configuration in your HOME folder.");
            bool? @continue = GetBooleanUserInput("Do you want to continue?");
            if (@continue != true)
            {
                return 2;
            }

            WriteLine();
            string domain = GetStringUserInput("Provide your domain");
            if (domain == null)
            {
                return 3;
            }

            string email = GetStringUserInput("Provide your e-mail");
            if (email == null)
            {
                return 4;
            }

            // run apt-get update
            PrintRunningProcess("Running apt-get update...");
            if (!RunProcess("apt-get", "update"))
            {
                PrintError("Failed to run apt-get update command! Do you run this configurator as root?");
                return 5;
            }

            ForegroundColor = ConsoleColor.Green;
            WriteLine("apt-get update command finished!");
            ResetColor();


            // install nginx package
            PrintRunningProcess("Installing nginx...");
            if (!RunProcess("apt-get", "install nginx"))
            {
                PrintError("Failed to install nginx package! Do you run this configurator as root?");
                return 6;
            }

            ForegroundColor = ConsoleColor.Green;
            WriteLine("nginx package installed!");
            ResetColor();


            // install letsencrypt package
            PrintRunningProcess("Installing Let's encrypt...");
            if (!RunProcess("apt-get", "install letsencrypt"))
            {
                PrintError("Failed to install Let's Encrypt package! Do you run this configurator as root?");
                return 7;
            }

            ForegroundColor = ConsoleColor.Green;
            WriteLine("Let's Encrypt package installed!");
            ResetColor();


            // backup nginx configuration
            PrintRunningProcess("Backing up nginx configuration...");
            if (!BackupNginxConfig())
            {
                PrintError("Failed to back up nginx configuration!");
                return 8;
            }


            // create new config for nginx to enable Let's Encrypt challenge validation
            PrintRunningProcess("Creating nginx sites-available configuration...");
            if (!CreateNginxSitesAvailableConfigForSettingUpLetsEncrypt())
            {
                PrintError("Failed to create nginx sites-available configuration!");
                return 9;
            }


            // run nginx config verifier
            PrintRunningProcess("Checking nginx configuration...");
            if (!RunProcess("nginx", "-t"))
            {
                PrintError("Failed nginx configuration checker test!");
                return 10;
            }

            ForegroundColor = ConsoleColor.Green;
            WriteLine("nginx configuration checker test is passed!");
            ResetColor();


            // restart nginx service
            PrintRunningProcess("Restarting nginx...");
            if (!RunProcess("service", "nginx restart"))
            {
                PrintError("Failed to restart nginx service!");
                return 11;
            }

            Thread.Sleep(TimeSpan.FromSeconds(2));
            ForegroundColor = ConsoleColor.Green;
            WriteLine("nginx restarted!");
            ResetColor();


            // run certbot
            string args = Regex.IsMatch(domain, @"www") ? domain : $"{domain},www.{domain}";
            PrintRunningProcess("Requesting SSL certificate...");
#if DEBUG
            if (!RunProcess("letsencrypt", $"certonly --non-interactive --authenticator webroot --webroot-path=/var/www/html --domains {args} --staging --dry-run --email {email}"))
#else
            if (!RunProcess("letsencrypt", $"certonly --non-interactive --authenticator webroot --webroot-path=/var/www/html --domains {args} --email {email} --agree-tos"))
#endif
            {
                PrintError("Failed to obtain SSL certificate from Let's Encrypt!");
                return 12;
            }

            ForegroundColor = ConsoleColor.Green;
            WriteLine("SSL certificate obtained!");
            ResetColor();


            // get dhparam with 2048-bit group
            PrintRunningProcess("Generating strong Diffie-Hellman 2048-bit group...");
            if (!RunProcess("openssl", "dhparam -out /etc/ssl/certs/dhparam.pem 2048"))
            {
                PrintError("Failed to generate strong Diffie-Hellman 2048-bit group!");
                return 12;
            }

            ForegroundColor = ConsoleColor.Green;
            WriteLine("Diffie-Hellman 2048-bit group generated!");
            ResetColor();


            // copy snippet config file
            PrintRunningProcess("Copying nginx SSL configuration file...");
            if (!CopyNginxSslConfiguration(domain))
            {
                PrintError("Failed to copy nginx SSL configuration file!");
                return 13;
            }


            // create final nginx config
            PrintRunningProcess("Generating new nginx sites-available configuration...");
            if (!CreateNginxSitesAvailableConfigFinal(domain))
            {
                PrintError("Failed to generate new nginx sites-available configuration!");
                return 14;
            }


            // run nginx config verifier
            PrintRunningProcess("Checking nginx configuration...");
            if (!RunProcess("nginx", "-t"))
            {
                PrintError("Failed nginx configuration checker test!");
                return 15;
            }

            ForegroundColor = ConsoleColor.Green;
            WriteLine("nginx configuration checker test is passed!");
            ResetColor();


            // restart nginx service
            PrintRunningProcess("Restarting nginx...");
            if (!RunProcess("service", "nginx restart"))
            {
                PrintError("Failed to restart nginx service!");
                return 16;
            }

            Thread.Sleep(TimeSpan.FromSeconds(2));
            ForegroundColor = ConsoleColor.Green;
            WriteLine("nginx restarted!");
            ResetColor();


            // schedule auto renew
            PrintRunningProcess("Scheduling Let's Encrypt to auto renew the certficate...");
            if (!ScheduleAutoRenew())
            {
                PrintError("Failed to schedule the auto renewal of the certificate! Please do this manually using crontab -e command.");
            }
            else
            {
                // restart cron service
                PrintRunningProcess("Restarting cron...");
                if (!RunProcess("service", "cron restart"))
                {
                    PrintError("Failed to restart cron service!");
                }
            }

            PrintSuccess("Successfully configure Let's Encrypt! To add new location entry to the nginx configuration, just add new .location.conf file inside /etc/nginx/snippets/ folder.");
            WriteLine("Press any key to exit...");

            ReadKey();
            return 0;
        }

        private static void PrintRunningProcess(string message)
        {
            _count++;
            string newMessage = $"{_count:00} => {message}";

            WriteLine();
            WriteLine(Enumerable.Repeat('*', newMessage.Length).ToArray());
            WriteLine(newMessage);
            WriteLine(Enumerable.Repeat('*', newMessage.Length).ToArray());
            WriteLine();
        }

        private static bool RunProcess(string processName, string args)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = processName,
                Arguments = args,
                CreateNoWindow = true
            };

            Process process = Process.Start(startInfo);
            process.WaitForExit();

            return process.ExitCode == 0;
        }

        private static Task<bool> RunProcess(string processName, string args, string successReturn, string failReturn)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = processName,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            Process process = new Process();
            process.StartInfo = startInfo;
            process.OutputDataReceived += (sender, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data))
                {
                    return;
                }

                if (e.Data.Contains(successReturn))
                {
                    tcs.SetResult(true);
                }

                if (e.Data.Contains(failReturn))
                {
                    tcs.SetResult(false);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data))
                {
                    return;
                }

                if (e.Data.Contains(successReturn))
                {
                    tcs.SetResult(true);
                }

                if (e.Data.Contains(failReturn))
                {
                    tcs.SetResult(false);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            return tcs.Task;
        }

        private static void PrintWarning(string message)
        {
            string[] lines = Regex.Split(Wrap(message, 70), Environment.NewLine);
            WriteLine("******************************************************************************");
            WriteLine("*                                                                            *");
            WriteLine("*         ██╗    ██╗ █████╗ ██████╗ ███╗   ██╗██╗███╗   ██╗ ██████╗          *");
            WriteLine("*         ██║    ██║██╔══██╗██╔══██╗████╗  ██║██║████╗  ██║██╔════╝          *");
            WriteLine("*         ██║ █╗ ██║███████║██████╔╝██╔██╗ ██║██║██╔██╗ ██║██║  ███╗         *");
            WriteLine("*         ██║███╗██║██╔══██║██╔══██╗██║╚██╗██║██║██║╚██╗██║██║   ██║         *");
            WriteLine("*         ╚███╔███╔╝██║  ██║██║  ██║██║ ╚████║██║██║ ╚████║╚██████╔╝         *");
            WriteLine("*          ╚══╝╚══╝ ╚═╝  ╚═╝╚═╝  ╚═╝╚═╝  ╚═══╝╚═╝╚═╝  ╚═══╝ ╚═════╝          *");
            WriteLine("*                                                                            *");

            foreach (string line in lines)
            {
                WriteLine($"*   {line,-70}   *");
            }

            WriteLine("*                                                                            *");
            WriteLine("******************************************************************************");
            WriteLine();
        }

        private static void PrintError(string message)
        {
            string[] lines = Regex.Split(Wrap(message, 70), Environment.NewLine);
            WriteLine("******************************************************************************");
            WriteLine("*                                                                            *");
            WriteLine("*                 ███████╗██████╗ ██████╗  ██████╗ ██████╗                   *");
            WriteLine("*                 ██╔════╝██╔══██╗██╔══██╗██╔═══██╗██╔══██╗                  *");
            WriteLine("*                 █████╗  ██████╔╝██████╔╝██║   ██║██████╔╝                  *");
            WriteLine("*                 ██╔══╝  ██╔══██╗██╔══██╗██║   ██║██╔══██╗                  *");
            WriteLine("*                 ███████╗██║  ██║██║  ██║╚██████╔╝██║  ██║                  *");
            WriteLine("*                 ╚══════╝╚═╝  ╚═╝╚═╝  ╚═╝ ╚═════╝ ╚═╝  ╚═╝                  *");
            WriteLine("*                                                                            *");

            foreach (string line in lines)
            {
                WriteLine($"*   {line,-70}   *");
            }

            WriteLine("*                                                                            *");
            WriteLine("******************************************************************************");
            WriteLine();
        }

        private static void PrintSuccess(string message)
        {
            string[] lines = Regex.Split(Wrap(message, 70), Environment.NewLine);
            WriteLine("******************************************************************************");
            WriteLine("*          ███████╗██╗   ██╗ ██████╗ ██████╗███████╗███████╗███████╗         *");
            WriteLine("*          ██╔════╝██║   ██║██╔════╝██╔════╝██╔════╝██╔════╝██╔════╝         *");
            WriteLine("*          ███████╗██║   ██║██║     ██║     █████╗  ███████╗███████╗         *");
            WriteLine("*          ╚════██║██║   ██║██║     ██║     ██╔══╝  ╚════██║╚════██║         *");
            WriteLine("*          ███████║╚██████╔╝╚██████╗╚██████╗███████╗███████║███████║         *");
            WriteLine("*          ╚══════╝ ╚═════╝  ╚═════╝ ╚═════╝╚══════╝╚══════╝╚══════╝         *");
            WriteLine("*                                                                            *");

            foreach (string line in lines)
            {
                WriteLine($"*   {line,-70}   *");
            }

            WriteLine("*                                                                            *");
            WriteLine("******************************************************************************");
            WriteLine();
        }

        // thanks to code provided here: http://stackoverflow.com/questions/17586/best-word-wrap-algorithm
        private static string Wrap(string str, int width)
        {
            string[] words = Explode(str, SplitChars);

            int curLineLength = 0;
            StringBuilder strBuilder = new StringBuilder();
            for(int i = 0; i < words.Length; i += 1)
            {
                string word = words[i];
                // If adding the new word to the current line would be too long,
                // then put it on a new line (and split it up if it's too long).
                if ((curLineLength + word.Length) > width)
                {
                    // Only move down to a new line if we have text on the current line.
                    // Avoids situation where wrapped whitespace causes emptylines in text.
                    if (curLineLength > 0)
                    {
                        strBuilder.Append(Environment.NewLine);
                        curLineLength = 0;
                    }

                    // If the current word is too long to fit on a line even on it's own then
                    // split the word up.
                    while (word.Length > width)
                    {
                        strBuilder.Append(word.Substring(0, width - 1) + "-");
                        word = word.Substring(width - 1);

                        strBuilder.Append(Environment.NewLine);
                    }

                    // Remove leading whitespace from the word so the new line starts flush to the left.
                    word = word.TrimStart();
                }

                strBuilder.Append(word);
                curLineLength += word.Length;
            }

            return strBuilder.ToString();
        }

        // thanks to code provided here: http://stackoverflow.com/questions/17586/best-word-wrap-algorithm
        private static string[] Explode(string str, char[] splitChars)
        {
            List<string> parts = new List<string>();
            int startIndex = 0;
            while (true)
            {
                int index = str.IndexOfAny(splitChars, startIndex);

                if (index == -1)
                {
                    parts.Add(str.Substring(startIndex));
                    return parts.ToArray();
                }

                string word = str.Substring(startIndex, index - startIndex);
                char nextChar = str.Substring(index, 1)[0];

                // Dashes and the likes should stick to the word occuring before it. Whitespace doesn't have to.
                if (char.IsWhiteSpace(nextChar))
                {
                    parts.Add(word);
                    parts.Add(nextChar.ToString());
                }
                else
                {
                    parts.Add(word + nextChar);
                }

                startIndex = index + 1;
            }
        }

        private static bool? GetBooleanUserInput(string prompt)
        {
            while (true)
            {
                Write($"{prompt} (y/n/cancel): ");
                string input = ReadLine();
                if (input?.Trim().Equals("cancel", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return null;
                }
                if (input?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return true;
                }
                if (input?.Trim().Equals("n", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return false;
                }

                WriteLine();
                WriteLine(@"INVALID INPUT!");
                WriteLine();
            }
        }

        private static string GetStringUserInput(string prompt)
        {
            while (true)
            {
                Write($"{prompt} (leave blank to cancel): ");
                string input = ReadLine();
                return string.IsNullOrWhiteSpace(input) ? null : input;
            }
        }

        private static bool BackupNginxConfig()
        {
            const string path = "/etc/nginx/";
            string backupPath = Path.Combine(_homePath, "nginx-backup", DateTime.Now.ToString("yyyyMMddHHmm"));

            try
            {
                WriteLine("Backing up existing nginx configuration to HOME folder...");
                if (!Directory.Exists(path))
                {
                    ForegroundColor = ConsoleColor.Red;
                    WriteLine($"nginx configuration does not exist at path {path}!");
                    ResetColor();

                    bool? @continue = GetBooleanUserInput("Do you want to continue without backing up the file(s)?");
                    return @continue == true;
                }

                Directory.CreateDirectory(backupPath);
                CopyDirectory(path, backupPath);

                ForegroundColor = ConsoleColor.Green;
                WriteLine("nginx configuration backed up to HOME folder!");
                ResetColor();

                return true;
            }

            catch (Exception)
            {
                return false;
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            DirectoryInfo sourceDirectoryInfo = new DirectoryInfo(source);
            foreach (FileInfo fileInfo in sourceDirectoryInfo.EnumerateFiles())
            {
                string path = Path.Combine(destination, fileInfo.Name);
                fileInfo.CopyTo(path, true);
            }

            foreach (DirectoryInfo directoryInfo in sourceDirectoryInfo.EnumerateDirectories())
            {
                string path = Path.Combine(destination, directoryInfo.Name);
                Directory.CreateDirectory(path);
                CopyDirectory(directoryInfo.FullName, path);
            }
        }

        private static bool CreateNginxSitesAvailableConfigForSettingUpLetsEncrypt()
        {
            try
            {
                WriteLine("Creating configuration file...");

                if (!Directory.Exists(SitesAvailableDirectory))
                {
                    Directory.CreateDirectory(SitesAvailableDirectory);
                }

                using (StreamReader reader = new StreamReader(File.OpenRead(Path.Combine(_appPath, "default.conf"))))
                {
                    string content = reader.ReadToEnd();
                    using (StreamWriter writer = new StreamWriter(File.Open(SitesAvailableDefaultConfigPath, FileMode.Create, FileAccess.Write)))
                    {
                        writer.Write(content);
                    }
                }

                ForegroundColor = ConsoleColor.Green;
                WriteLine("Configuration file created!");
                ResetColor();

                return true;
            }

            catch (Exception)
            {
                return false;
            }
        }

        private static bool CopyNginxSslConfiguration(string domain)
        {
            try
            {
                string domainSnippetPath = string.Format(SslDomainSnippetConfigPathFormat, domain);
                if (File.Exists(domainSnippetPath))
                {
                    File.Delete(domainSnippetPath);
                }

                if (File.Exists(SslParamsSnippetConfigPath))
                {
                    File.Delete(SslParamsSnippetConfigPath);
                }

                using (StreamReader reader = new StreamReader(File.OpenRead(Path.Combine(_appPath, "ssl-snippet.conf"))))
                {
                    string content = reader.ReadToEnd();
                    content = string.Format(content, domain);

                    using (StreamWriter writer = new StreamWriter(File.Open(domainSnippetPath, FileMode.Create, FileAccess.Write)))
                    {
                        writer.Write(content);
                    }
                }

                ForegroundColor = ConsoleColor.Green;
                WriteLine($"Created file {Path.GetFileName(domainSnippetPath)}!");
                ResetColor();

                using (StreamReader reader = new StreamReader(File.OpenRead(Path.Combine(_appPath, "ssl-params-snippet.conf"))))
                {
                    string content = reader.ReadToEnd();
                    content = string.Format(content, domain);

                    using (StreamWriter writer = new StreamWriter(File.Open(SslParamsSnippetConfigPath, FileMode.Create, FileAccess.Write)))
                    {
                        writer.Write(content);
                    }
                }

                ForegroundColor = ConsoleColor.Green;
                WriteLine($"Created file {Path.GetFileName(SslParamsSnippetConfigPath)}!");
                ResetColor();

                return true;
            }

            catch (Exception)
            {
                return false;
            }
        }

        private static bool CreateNginxSitesAvailableConfigFinal(string domain)
        {
            try
            {
                WriteLine("Creating new configuration file...");
                using (StreamReader reader = new StreamReader(File.OpenRead(Path.Combine(_appPath, "default-after.conf"))))
                {
                    string content = reader.ReadToEnd();
                    content = string.Format(content, domain);

                    using (StreamWriter writer = new StreamWriter(File.Open(SitesAvailableDefaultConfigPath, FileMode.Create, FileAccess.Write)))
                    {
                        writer.Write(content);
                    }
                }

                ForegroundColor = ConsoleColor.Green;
                WriteLine("Configuration file created!");
                ResetColor();

                return true;
            }

            catch (Exception)
            {
                return false;
            }
        }

        private static bool ScheduleAutoRenew()
        {
            try
            {
                WriteLine("Generating cron file...");
                using (StreamReader reader = new StreamReader(File.OpenRead(Path.Combine(_appPath, "cron"))))
                {
                    string content = reader.ReadToEnd();
                    using (StreamWriter writer = new StreamWriter(File.Open(LetsEncryptCronFilePath, FileMode.Create, FileAccess.Write)))
                    {
                        writer.Write(content);
                    }
                }

                ForegroundColor = ConsoleColor.Green;
                WriteLine("Configuration file created!");
                ResetColor();

                return true;
            }

            catch (Exception)
            {
                return false;
            }
        }
    }
}
