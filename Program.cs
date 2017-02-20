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
        private const string SitesAvailableDirectory = "/etc/nginx/sites-available/";
        private const string SitesAvailableDefaultConfigPath = SitesAvailableDirectory + "default";
        private static readonly char[] SplitChars = { ' ', '-', '\t' };
        private static readonly string HomePath = Environment.GetEnvironmentVariable("HOME");
        private static readonly string AppPath = PlatformServices.Default.Application.ApplicationBasePath;


        private static int _count;

        public static void Main()
        {
            PrintWarning("Your existing nginx configuration will be replaced! The program will make a backup of your current configuration in your HOME folder.");
            bool? @continue = GetBooleanUserInput("Do you want to continue?");
            if (@continue != true)
            {
                return;
            }

            WriteLine();

            string domain = GetStringUserInput("Provide your domain");
            if (domain == null)
            {
                return;
            }

            PrintRunningProcess("Running apt-get update...");
            RunProcess("apt-get", "update");

            PrintRunningProcess("Installing Let's encrypt...");
            RunProcess("apt-get", "install letsencrypt");

            PrintRunningProcess("Creating nginx sites-available configuration...");
            CreateNginxSitesAvailableConfigForSettingUpLetsEncrypt();

            PrintRunningProcess("Checking nginx configuration...");
            if (!RunProcess("nginx", "-t", "success", "fail").Result)
            {
                PrintError("Failed nginx configuration checker test!");
                return;
            }

            WriteLine("nginx configuration checker test is passed!");

            PrintRunningProcess("Restarting nginx...");
            RunProcess("service", "nginx restart");
            Thread.Sleep(TimeSpan.FromSeconds(5));
            WriteLine("nginx restarted!");

            string args = Regex.IsMatch(domain, @"www") ? domain : $"{domain} -d www.{domain}";
            PrintRunningProcess("Requesting SSL certificate...");
            RunProcess("letsencrypt", $"certonly -n -a webroot --webroot-path=/var/www/html -d {args} --register-unsafely-without-email --staging");
            WriteLine("SSL certificate obtained!");
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

        private static void RunProcess(string processName, string args)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = processName,
                Arguments = args,
                CreateNoWindow = true
            };

            Process process = Process.Start(startInfo);
            process.WaitForExit();
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
            WriteLine("*                 ███████╗██████╗ ██████╗  ██████╗ ██████╗                  *");
            WriteLine("*                 ██╔════╝██╔══██╗██╔══██╗██╔═══██╗██╔══██╗                 *");
            WriteLine("*                 █████╗  ██████╔╝██████╔╝██║   ██║██████╔╝                 *");
            WriteLine("*                 ██╔══╝  ██╔══██╗██╔══██╗██║   ██║██╔══██╗                 *");
            WriteLine("*                 ███████╗██║  ██║██║  ██║╚██████╔╝██║  ██║                 *");
            WriteLine("*                 ╚══════╝╚═╝  ╚═╝╚═╝  ╚═╝ ╚═════╝ ╚═╝  ╚═╝                 *");
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

        private static void CreateNginxSitesAvailableConfigForSettingUpLetsEncrypt()
        {
            WriteLine("Creating configuration file...");

            if (!Directory.Exists(SitesAvailableDirectory))
            {
                Directory.CreateDirectory(SitesAvailableDirectory);
            }

            using (StreamReader reader = new StreamReader(File.OpenRead(Path.Combine(AppPath, "default"))))
            {
                string content = reader.ReadToEnd();
                using (StreamWriter writer = new StreamWriter(File.Open(SitesAvailableDefaultConfigPath, FileMode.Create, FileAccess.Write)))
                {
                    writer.Write(content);
                }
            }

            WriteLine("Configuration file created!");
        }
    }
}
