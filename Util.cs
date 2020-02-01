/*
 * Created by SharpDevelop.
 * User: user
 * Date: 14.01.2014
 * Time: 18:01
 * 
 */
//#define __DEBUG

using System;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.IO.Compression;
using System.Xml.Serialization;
using System.Diagnostics;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.Win32;
using System.Net;
using System.Linq.Expressions;


namespace Updater
{
    /// @author Matthias Gander
    /// @date 12.12.2013
    /// <summary>
    /// Also a little helper class, directly out of StackOverflow to allow us to annotate enums (useful for error handling)
    /// </summary>
    public class StringValue : System.Attribute
    {
        private string _value;

        public StringValue(string value)
        {
            _value = value;
        }

        public string Value
        {
            get { return _value; }
        }
    }

    /// @author Matthias Gander
    /// @date 12.12.2013
    /// <summary>
    /// A little helper class, directly out of StackOverflow to allow us to annotate enums (useful for error handling) 
    /// </summary>
    public static class StringEnum
    {
        public static string GetStringValue(Enum value)
        {
            string output = null;
            Type type = value.GetType();

            //Check first in our cached results...

            //Look for our 'StringValueAttribute'

            //in the field's custom attributes

            System.Reflection.FieldInfo fi = type.GetField(value.ToString());
            StringValue[] attrs =
                fi.GetCustomAttributes(typeof(StringValue),
                                       false) as StringValue[];
            if (attrs.Length > 0)
            {
                output = attrs[0].Value;
            }

            return output;
        }

        public static String GetInternalString(Enum val)
        {
            return StringEnum.GetStringValue(val);
        }

        public static T ParseEnum<T>(string value)
        {
            return (T)Enum.Parse(typeof(T), value, true);
        }

        //Errors errlvl = EnumUtil.ParseEnum<Errors>("ERROR_XXX_YYY");
    }
    /// @author Matthias Gander
    /// @date 12.12.2013
    /// <summary>
    /// Utility class that provides a lot of useful functions, generating SHA tokens, parsing tokens from XML, serializing, etc.
    /// </summary>
    public static class Util
    {
        public enum PrintMode
        {
            ERROR,  // Error Messages
            NORMAL, // Normal Messages
            VARS    // Variables
        };

        public static void PrintVars(String [] varnames, Object []values)
        {
            string message = "";

            if (varnames.Length != values.Length)
                return;


            for (int i = 0; i < varnames.Length; i++)
            {
                message += varnames[i];
                message += ":";
                message += values[i].ToString(); 
            }

            PrintInformation(Configuration.InformationStrings.NONE, message, PrintMode.VARS, false);
        }

        public static string GetMemberName<T>(Expression<Func<T>> memberExpression)
        {
            MemberExpression expressionBody = (MemberExpression)memberExpression.Body;
            return expressionBody.Member.Name;
        }

        
        /// <summary>
        /// CAREFUL: THIS IS A __DEBUG METHOD, WORKS BEST ON __DEBUG FLAG
        /// Prints information message by code, if isError then as error string.
        /// Exits program on exit == true
        /// Optionally prints appended string
        /// </summary>
        /// <param name="code"></param>
        /// <param name="isError"></param>
        /// <param name="exit"></param>
        public static void PrintInformation(Configuration.InformationStrings code, string optstring, PrintMode mode, bool exit)
        {
            MethodBase m = new StackTrace().GetFrame(1).GetMethod();
            string methodName = "N/A";
            string className = "N/A";
            string logMessage = "";
            if (m != null)
            {
                methodName = m.Name;
                className = m.ReflectedType.Name;

            }

            switch (mode)
            {
                case PrintMode.ERROR:
                    logMessage += "ERROR:";
#if __DEBUG
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("ERROR: ");
#endif
                    break;
                case PrintMode.VARS:
                    logMessage += "VARIABLES:";
#if __DEBUG
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("VARIABLES: ");
#endif
                    break;
                default:
                    break;
            }

            logMessage += ":"+ className + ":" + methodName + ": " + StringEnum.GetInternalString(code) + " ";


#if __DEBUG   
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("{0,-35}", ":"+className + ":" + methodName + ": ");
           // Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(StringEnum.GetInternalString(code));
            //Console.ResetColor();
#endif


            if (!String.IsNullOrEmpty(optstring))
            {
#if __DEBUG
                Console.Write(optstring);
#endif
                logMessage += optstring;
            }

#if __DEBUG
            Console.Write("\n");
#endif

            logMessage += "\n";
            Logger.Logger.Log(logMessage);

            if (exit)
            {
#if __DEBUG
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("Exiting.");
#endif
                Logger.Logger.Log("Exiting.");

#if __DEBUG 
                Console.ReadKey();
#endif
                Environment.Exit(0);
            }
        }

        /// <summary>
        /// Check if version string is indeed corresponding to the correct version pattern x.y.z.w
        /// </summary>
        /// <param name="versionString"></param>
        /// <returns></returns>
        public static bool RegularVersion(string versionString)
        {
            bool val = false;
            if (!string.IsNullOrEmpty(versionString))
            {
                string pattern = "^([0-9]*\\.){3}[0-9]*$";
                System.Text.RegularExpressions.Regex re = new System.Text.RegularExpressions.Regex(pattern);
                val = re.IsMatch(versionString);
            }
            return val;
        }


        /// <summary>
        /// Compares versions 
        /// </summary>
        /// <returns>
        /// 1 if firstVersion is newer than secondVersion, 
        /// 2 if secondVersion is newer than firstVersion, 
        /// 0 on equal, 
        /// -1 if lengths do not match
        /// -2 if version strings are corrupt
        /// </returns>
        /// <param name="version">Version.</param>
        /// <param name="version2">Version2.</param>
        public static int CompareVersion(string firstVersion, string secondVersion)
        {

            Util.PrintInformation(Configuration.InformationStrings.NONE, "FirstVersion:" + firstVersion + " " + "SecondVersion: " + secondVersion, Util.PrintMode.NORMAL, false);

            if (!(RegularVersion(firstVersion)) || !(RegularVersion(secondVersion)))
            {
                PrintInformation(Configuration.InformationStrings.INFORMATION_000_002, null, Util.PrintMode.ERROR, false);
                return -2;
            }


            string[] stringFirstVersionArray = firstVersion.Split(new char[] { '.' });
            string[] stringSecondVersionArray = secondVersion.Split(new char[] { '.' });
            int length = stringFirstVersionArray.Length;
            int length2 = stringSecondVersionArray.Length;

            if (length != length2)
            {
                PrintInformation(Configuration.InformationStrings.INFORMATION_000_010, null, Util.PrintMode.ERROR, false);
                return -1;
            }

            try
            {
                for (int i = 0; i < length; i++)
                {
                    if (Convert.ToInt32(stringFirstVersionArray[i]) > Convert.ToInt32(stringSecondVersionArray[i]))
                        return 1;
                    else if (Convert.ToInt32(stringFirstVersionArray[i]) < Convert.ToInt32(stringSecondVersionArray[i]))
                        return 2;
                }
            }
            catch (FormatException) { return -2; }
            catch (OverflowException) { return -2; }

            return 0;
        }


        public static bool StartBinary(string binary)
        {
            if (String.IsNullOrEmpty(binary))
            {

                PrintInformation(Configuration.InformationStrings.NONE, "No name provided for binary", Util.PrintMode.ERROR, false);
       
                return false;
            }
            try
            {
                PrintInformation(Configuration.InformationStrings.NONE, "Starting Binary: " + binary, Util.PrintMode.NORMAL, false);
                Process.Start(binary);
                return true;
            }
            catch (ObjectDisposedException e) { PrintInformation(Configuration.InformationStrings.NONE, e.Message, Util.PrintMode.ERROR, true); }
            catch (FileNotFoundException e) { PrintInformation(Configuration.InformationStrings.NONE, e.Message, Util.PrintMode.ERROR, true); }
            catch (Win32Exception e) { PrintInformation(Configuration.InformationStrings.NONE, e.Message, Util.PrintMode.ERROR, true); }
            return false;
        }

        public static bool RemoveAutostartEntry(string autostartBinary, string path)
        {
            try
            {
                // The path to the key where Windows looks for startup applications
                RegistryKey rkApp = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);

                // Add the value in the registry so that the application runs at startup
                rkApp.DeleteValue(autostartBinary, true);
                return true;
           }
            catch (System.ArgumentException sa)
            {
                Util.PrintInformation(Configuration.InformationStrings.NONE, sa.Message, Util.PrintMode.ERROR, false);
                return false;
            }
            catch (System.ObjectDisposedException ode)
            {
                Util.PrintInformation(Configuration.InformationStrings.NONE,ode.Message, Util.PrintMode.ERROR, false);
                return false;
            }
            catch (System.UnauthorizedAccessException uae)
            {
                Util.PrintInformation(Configuration.InformationStrings.NONE, uae.Message, Util.PrintMode.ERROR, false);
                return false;
            }
            catch (SystemException se)
            {
                Util.PrintInformation(Configuration.InformationStrings.NONE, se.Message, Util.PrintMode.ERROR, false);
                return false;
            }
        }

        /// <summary>
        /// Creates an autostart entry for a given binary and a path
        /// </summary>
        /// <param name="autostartBinary">binary to be started</param>
        /// <param name="path">path to the binary</param>
        /// <returns>true if successfull</returns>
        public static bool CreateAutostartEntry(string autostartBinary, string path)
        {
            // Add the value in the registry so that the application runs at startup
            try
            {
                // The path to the key where Windows looks for startup applications
                RegistryKey rkApp = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                rkApp.SetValue(autostartBinary, path);
                return true;
            }
            catch (System.ArgumentException sa)
            {
                Util.PrintInformation(Configuration.InformationStrings.NONE, sa.Message, Util.PrintMode.ERROR, false);
                return false;
            }
            catch (System.ObjectDisposedException ode)
            {
                Util.PrintInformation(Configuration.InformationStrings.NONE, ode.Message, Util.PrintMode.ERROR, false);
                return false;
            }
            catch (System.UnauthorizedAccessException uae)
            {
                Util.PrintInformation(Configuration.InformationStrings.NONE, uae.Message, Util.PrintMode.ERROR, false);
                return false;
            }
            catch (SystemException se)
            {
                Util.PrintInformation(Configuration.InformationStrings.NONE, se.Message, Util.PrintMode.ERROR, false);
                return false;
            }
        }

        public static string[] GetDirectoryEntries(string path)
        {
            try
            {
                // Obtain the file system entries in the directory path. 
                string[] directoryFullPathEntries = System.IO.Directory.GetDirectories(path);

                if (directoryFullPathEntries.Length < 1)
                    return null;

                string[] directoryPathEntries = new string[directoryFullPathEntries.Length];

                for (int i = 0; i < directoryFullPathEntries.Length; i++)
                {
                    directoryPathEntries[i] = Path.GetFileName(directoryFullPathEntries[i]);
                }
                return directoryPathEntries;
            }

            catch (ArgumentNullException)
            {
                Util.PrintInformation(Configuration.InformationStrings.INFORMATION_000_011, "", Util.PrintMode.ERROR, false);
            }
            catch (System.Security.SecurityException)
            {
                Util.PrintInformation(Configuration.InformationStrings.INFORMATION_000_012, "", Util.PrintMode.ERROR, false);
            }
            catch (ArgumentException)
            {
                Util.PrintInformation(Configuration.InformationStrings.INFORMATION_000_013, "", Util.PrintMode.ERROR, false);
            }
            catch (System.IO.DirectoryNotFoundException)
            {
                Util.PrintInformation(Configuration.InformationStrings.INFORMATION_000_014, "", Util.PrintMode.ERROR, false);
            }
            return null;
        }

        /// <summary>
        /// Sometimes we have to get rid of a pesky processes (mostly the client_check process).
        /// Why? In order not to spam the task switcher with dead processes.
        /// </summary>
        public static void KillProcesses(string processName)
        {
            Process[] localByName = Process.GetProcessesByName(processName);

            foreach (Process p in localByName)
            {

                PrintInformation(Configuration.InformationStrings.NONE, p.ProcessName + " " + p.Id, Util.PrintMode.NORMAL, false);

                try
                {
                    p.CloseMainWindow();// Kill();
                }
                catch (Win32Exception s) { PrintInformation(Configuration.InformationStrings.NONE, s.Message + " " + p.ProcessName, Util.PrintMode.ERROR, false); }
                catch (NotSupportedException n) { PrintInformation(Configuration.InformationStrings.NONE, n.Message + " " + p.ProcessName, Util.PrintMode.ERROR, false); }
                catch (InvalidOperationException i) { PrintInformation(Configuration.InformationStrings.NONE, i.Message + " " + p.ProcessName, Util.PrintMode.ERROR, false); }
                catch (Exception e) { PrintInformation(Configuration.InformationStrings.NONE, "Unknown Error: "+e.Message + " " + p.ProcessName, Util.PrintMode.ERROR, false); }
            }
        }

        public static void PrintFileSystemEntries(string path)
        {
            try
            {
                // Obtain the file system entries in the directory path. 
                string[] directoryEntries = System.IO.Directory.GetFileSystemEntries(path);

                foreach (string str in directoryEntries)
                {
                    Util.PrintInformation(Configuration.InformationStrings.NONE, str, Util.PrintMode.NORMAL, false);
                }
            }
            catch (Exception e)
            {
                if (e is ArgumentException)
                    Util.PrintInformation(Configuration.InformationStrings.INFORMATION_000_011, "", Util.PrintMode.ERROR, false);
                else if (e is System.Security.SecurityException)
                    Util.PrintInformation(Configuration.InformationStrings.INFORMATION_000_012, "", Util.PrintMode.ERROR, false);
                else if (e is ArgumentNullException)
                    Util.PrintInformation(Configuration.InformationStrings.INFORMATION_000_013, "", Util.PrintMode.ERROR, false);
                else if (e is DirectoryNotFoundException)
                    Util.PrintInformation(Configuration.InformationStrings.INFORMATION_000_014, "", Util.PrintMode.ERROR, false);
                else
                    Util.PrintInformation(Configuration.InformationStrings.NONE, "Unknown problem, can't print directory", Util.PrintMode.ERROR, false);
            }
        }

        /// <summary>
        /// Delete temporary files
        /// </summary>
        /// <param name="tmp1"></param>
        /// <param name="tmp2"></param>
        public static bool DeleteTmpFiles(List<string> files)
        {
            if (files == null)
            {
                PrintInformation(Configuration.InformationStrings.NONE, "files is empty", Util.PrintMode.ERROR, false);
                return false;
            }
            foreach (string s in files)
            {
                PrintInformation(Configuration.InformationStrings.NONE, s, Util.PrintMode.NORMAL, false);

                try { File.Delete(s); }
                catch
                {
                    PrintInformation(Configuration.InformationStrings.INFORMATION_000_009, s, Util.PrintMode.ERROR, false);
                    return false;
                }

                // best effort
            }
            return true;
        }

        /// <summary>
        /// Extracts the largest version string from a directory for files corresponding to some pattern. The assumption is that
        /// the name is followed by a version pattern, e.g. ls -l ./ -> name_x.y.z.w, name_x.y.z.w' 
        /// </summary>
        /// <param name="path">Provides the path to the folder e.g. c:/folder/</param>
        /// <param name="pattern">Version pattern</param>
        /// <returns>Returns the newest version string, i.e. x.y.z.w', or null on error</returns>
        public static string ExtractVersionStringName(string path, string pattern)
        {
            string newestVersion = "0.0.0.0";
            if (string.IsNullOrEmpty(path) || InvalidPathCharacters(path)
                || !Directory.Exists(path) || string.IsNullOrEmpty(pattern))
            {
                PrintInformation(Configuration.InformationStrings.NONE, "Path/pattern poses problems:"+path+" "+pattern, Util.PrintMode.ERROR, false);
                return null;
            }

            try
            {
                // Obtain the file system entries in the directory path. 
                string[] directoryEntries = System.IO.Directory.GetFileSystemEntries(path);
         
                System.Text.RegularExpressions.Regex re = new System.Text.RegularExpressions.Regex(pattern);
          
                foreach (string str in directoryEntries)
                {

                    Util.PrintInformation(Configuration.InformationStrings.NONE, str, Util.PrintMode.NORMAL, false);

                    // Assuming we finde the pattern..
                    if (re.IsMatch(str))
                    {
                        // .. we extract the version
                        string tmp = new DirectoryInfo(str).Name;
                        tmp = tmp.Split('_')[1];
                        if (CompareVersion(tmp, newestVersion) == 1)
                            newestVersion = tmp;
#if __DEBUG
                        Console.ReadKey();
#endif
                    }
                }
            }
            catch (Exception e)
            {
                if (e is ArgumentNullException)
                    Util.PrintInformation(Configuration.InformationStrings.INFORMATION_000_011, "", Util.PrintMode.ERROR, false);
                else if (e is System.Security.SecurityException)
                    Util.PrintInformation(Configuration.InformationStrings.INFORMATION_000_012, "", Util.PrintMode.ERROR, false);
                else if (e is ArgumentException)
                    Util.PrintInformation(Configuration.InformationStrings.INFORMATION_000_013, "", Util.PrintMode.ERROR, false);
                else if (e is System.IO.DirectoryNotFoundException)
                    Util.PrintInformation(Configuration.InformationStrings.INFORMATION_000_014, "", Util.PrintMode.ERROR, false);
                else
                    Util.PrintInformation(Configuration.InformationStrings.NONE, "Uncaught error", Util.PrintMode.ERROR, false);
            }

            if (String.Compare("0.0.0.0", newestVersion) == 0)
            {
                PrintInformation(Configuration.InformationStrings.NONE, "Newest version has an invalid string:"+newestVersion, Util.PrintMode.ERROR, false);
                return null;
            }
            return newestVersion;
        }


        /// <summary>
        /// Check if version file exists and if it is a valid version file
        /// </summary>
        /// <param name="path"></param>
        /// <returns>Version string in form x.y.z.w, kill, or null</returns>
        public static string ExtractVersionStringFile(string path, Configuration.VersionMode vmode)
        {
            string[] lines = null;
            if (string.IsNullOrEmpty(path) || InvalidPathCharacters(path) || !new System.IO.FileInfo(path).Exists)
                return null;
            
            else if (new System.IO.FileInfo(path).Exists)
            {
                try
                {
                    lines = File.ReadAllLines(path);
                }
                catch (Exception e)
                {
                    PrintInformation(Configuration.InformationStrings.NONE, "Error while reading file, "+e.Message, Util.PrintMode.ERROR, false);
                    return null;
                }

                foreach (string s in lines)
                    PrintInformation(Configuration.InformationStrings.NONE, "Contents of file: " + s, Util.PrintMode.NORMAL, false);

                string stringPattern = "";
                switch (vmode)
                {
                    case Configuration.VersionMode.BINARY:
                        stringPattern = "bin ";
                        return ParseVersionString(lines, stringPattern);

                    case Configuration.VersionMode.CONFIGURATION:
                        stringPattern = "conf ";
                        return ParseVersionString(lines, stringPattern);
                    case Configuration.VersionMode.KILL:
                        return ParseKillString(lines);
                    default:
                        return null;
                }
            }

            PrintInformation(Configuration.InformationStrings.NONE, "Parsing. Invalid version file, a general error occurred", PrintMode.ERROR, false);
            return null;
        }

        private static string ParseKillString(string[] lines)
        {
            bool val = false;                                    
            string commentPattern = "#";                         ///< Comments start with a '#'
            string stringPattern = "kill";
            string pattern = "^" + stringPattern;
            string potentialKillString = "";

            System.Text.RegularExpressions.Regex re = new System.Text.RegularExpressions.Regex(pattern);
            LinkedList<String> commentStrings = new LinkedList<string>();

            foreach (string s in lines)
            {
                string trimString = s.TrimStart(' ', '\t');

                if (trimString.StartsWith(commentPattern))
                {
                    commentStrings.AddLast(trimString);
                }

                else if (re.IsMatch(trimString))
                {
                    val = true;
                    potentialKillString = trimString;
                }
            }

            PrintInformation(Configuration.InformationStrings.NONE, "Printing Comments:", PrintMode.NORMAL, false);
            foreach (string s in commentStrings)
            {
                PrintInformation(Configuration.InformationStrings.NONE, s, PrintMode.NORMAL, false);
            }

            if (!val)
            {
                PrintInformation(Configuration.InformationStrings.NONE, "Keep alive active.", PrintMode.ERROR, false);
                return null;
            }

            return potentialKillString;
        }

        private static string ParseVersionString(string [] lines, string stringPattern)
        {
            bool val = false;                                    
            string commentPattern = "#";                         ///< Comments start with a '#'
            string versionPattern = "([0-9]*\\.){3}[0-9]*$";     ///<  Version Pattern
                
            //////////////////////////////////////////7////////////////////
            // Extract comments and versions
            //////////////////////////////////////////7////////////////////

            string pattern = "^" + stringPattern + versionPattern;
            string potentialVersionString = "";
            System.Text.RegularExpressions.Regex re = new System.Text.RegularExpressions.Regex(pattern);

            LinkedList<String> commentStrings = new LinkedList<string>();

            foreach (string s in lines)
            {
                string trimString = s.TrimStart(' ', '\t');

                if (trimString.StartsWith(commentPattern))
                {
                    commentStrings.AddLast(trimString);
                }

                else if (re.IsMatch(trimString))
                {
                    val = true;
                    potentialVersionString = trimString;
                }
            }

            PrintInformation(Configuration.InformationStrings.NONE, "Printing Comments:", PrintMode.NORMAL, false);
            foreach (string s in commentStrings)
            {
                PrintInformation(Configuration.InformationStrings.NONE, s, PrintMode.NORMAL, false);
            }

            if (!val)
            {
                PrintInformation(Configuration.InformationStrings.NONE, "Parsing. Invalid version file, could not retrieve version string", PrintMode.ERROR, false);
                return null;
            }

            string[] splitVersionString = potentialVersionString.Split(new char[] { ' ' });

            if (splitVersionString.Length < 2)
            {
                PrintInformation(Configuration.InformationStrings.NONE, "Parsing. Invalid version file, potential version string can't be extracted", PrintMode.ERROR, false);
                return null;
            }
            return splitVersionString[1];
        }

        /// <summary>
        /// Compares a binaries file version (careful to supply the correct one) to a versionString.
        /// This method is needed to determine whether new downloaded binaries, that appear to be newer are in fact newer.
        /// The big picture is to prevent infinite update loops
        /// </summary>
        /// <param name="pathToBinary">Path to the binary that needs to be checked</param>
        /// <param name="versionString">The version string that is compared</param>
        /// <returns>2 right parameter is newer, 1 left one is newer, 0 equal, -1 error</returns>
        public static int CompareBinaryFileVersion(string pathToBinary, string versionString)
        {
            // Test whether binary (which we think is newer) is really newer than the current 
            // one (otherwise might lead to infinite loop of update calls)
            try
            {
                // If the new version is really new return the path
                PrintInformation(Configuration.InformationStrings.NONE, "Version String:" + versionString, Util.PrintMode.NORMAL, false);
                PrintInformation(Configuration.InformationStrings.NONE, "Path:"+pathToBinary, Util.PrintMode.NORMAL, false);
                

                FileVersionInfo NewTargetVersion = FileVersionInfo.GetVersionInfo(pathToBinary);
                PrintInformation(Configuration.InformationStrings.NONE, "Target Version (Binary):" + NewTargetVersion.FileVersion, Util.PrintMode.NORMAL, false);

                int comp = CompareVersion(NewTargetVersion.FileVersion, versionString);
                
                switch (comp)
                {
                    case 0:
                        PrintInformation(Configuration.InformationStrings.NONE, "Binary and other version are equal", Util.PrintMode.NORMAL, false);
                        return 0;
                    case 1:
                        PrintInformation(Configuration.InformationStrings.NONE, "Binary version seems to be newer", Util.PrintMode.NORMAL, false);
                        return 1;
                    case 2:
                        PrintInformation(Configuration.InformationStrings.NONE, "Other version seems to be newer", Util.PrintMode.NORMAL, false);
                        return 2;
                    default: 
                        PrintInformation(Configuration.InformationStrings.NONE, "Can't compare binary file version and other version", Util.PrintMode.ERROR, false);
                        return -1;
                }
            }
            catch (Exception e)
            {
                PrintInformation(Configuration.InformationStrings.NONE, e.Message, Util.PrintMode.ERROR, false);
                return -1;
            }
        }

        /// <summary>
        /// Generic file downloader
        /// </summary>
        /// <param name="link">Link to the resource</param>
        /// <param name="tmpPath">Path to where it should be downloaded to</param>
        /// <returns></returns>
        public static bool DownloadFile(string link, string tmpPath)
        {
            // Download version file
            using (var client = new WebClient())
            {
                try
                {
                    Util.PrintInformation(Configuration.InformationStrings.NONE, "Downloading:" + link + "to:" + tmpPath, Util.PrintMode.NORMAL, false);
                    client.DownloadFile(link, tmpPath);
                }
                catch (WebException w)
                {
                    Util.PrintInformation(Configuration.InformationStrings.NONE, w.Message, Util.PrintMode.ERROR, false);
                    return false;
                }
                catch (System.Security.SecurityException s)
                {
                    Util.PrintInformation(Configuration.InformationStrings.NONE, s.Message, Util.PrintMode.ERROR, false);
                    return false;
                }
                catch (Exception e)
                {
                    Util.PrintInformation(Configuration.InformationStrings.NONE, e.Message, Util.PrintMode.ERROR, false); return false;
                }
            }
            return true;
        }


        /// <summary>
        /// Extracts all files in a zip archive (and overwrites files, if they are present). Needs .NET 4.5
        /// </summary>
        /// <param name="setupFile">Full path to the setup_x.y.z.w.zip</param>
        /// <param name="extractionPath">Full path to where the contents should be extracted to</param>
        public static bool ExtractFiles(string setupFile, string extractionPath)
        {
            try
            {
                Directory.CreateDirectory(extractionPath);
            }
            catch
            {
                PrintInformation(Configuration.InformationStrings.NONE, "Can't create directory (maybe exists):" + extractionPath, Util.PrintMode.ERROR, false);
            }

            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(setupFile))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        PrintInformation(Configuration.InformationStrings.NONE, "Trying to extract:" + entry.Name, Util.PrintMode.NORMAL, false);
                        entry.ExtractToFile(Path.Combine(extractionPath, entry.FullName), true);
                    }
                }
            }
            catch (Exception e)
            {
                if (e is SystemException || e is ArgumentException)
                {
                    PrintInformation(Configuration.InformationStrings.NONE, "Can't extract:" + e.Message, Util.PrintMode.ERROR, false);
                    return false;
                }
            }
            return true;
        }

        public static bool CopyFile(string from, string to, bool overwrite)
        {
            PrintInformation(Configuration.InformationStrings.NONE, "FROM: " + from + " TO: " + to, Util.PrintMode.NORMAL, false);

            try
            {
                File.Copy(from, to, overwrite);
                return true;
            }
            catch (Exception e)
            {
                if (e is UnauthorizedAccessException || e is ArgumentException || e is PathTooLongException || 
                    e is DirectoryNotFoundException || e is FileNotFoundException || e is IOException || e is NotSupportedException)
                {
                    PrintInformation(Configuration.InformationStrings.NONE, e.Message, Util.PrintMode.ERROR, false);
                    return false;
                }
            }
            return false;
        }


        public static bool InvalidPathCharacters(string path)
        {
            char[] invalidPathChars = Path.GetInvalidPathChars();
            foreach (char c in path.ToCharArray())
            {
                foreach (char cc in invalidPathChars)
                {
                    if (c == cc)
                        return true;
                }
            }
            return false;
        }

        public static string GetDebugServer(string path, string setupid)
        {
            if (String.IsNullOrEmpty(path))
                return "http://[defunct]/"+setupid+"/UPDATE/";
            String debugFileName = "debug.0";
            string serverPattern = "server ";
            string ipPattern = @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b";
            string pattern = serverPattern+ipPattern;
            System.Text.RegularExpressions.Regex re = new System.Text.RegularExpressions.Regex(pattern);
            string line;
            String concreteFile = Path.Combine(path, debugFileName);
            System.IO.StreamReader file = null;
            try
            {
                file = new System.IO.StreamReader(concreteFile);

                while ((line = file.ReadLine()) != null)
                {
                    PrintInformation(Configuration.InformationStrings.NONE, line, Util.PrintMode.NORMAL, false);
                    if (re.IsMatch(line))
                    {
                        String tmp = line.Split(' ')[1];
                        return "http://" + tmp + "/UPDATE/";
                    }
                }
                file.Close();
            }
            catch (ArgumentException e) { PrintInformation(Configuration.InformationStrings.NONE, e.Message, Util.PrintMode.ERROR, false); }
            catch (FileNotFoundException e) { PrintInformation(Configuration.InformationStrings.NONE, e.Message, Util.PrintMode.ERROR, false); }
            catch (DirectoryNotFoundException e) { PrintInformation(Configuration.InformationStrings.NONE, e.Message, Util.PrintMode.ERROR, false); }
            catch (IOException e) { PrintInformation(Configuration.InformationStrings.NONE, e.Message, Util.PrintMode.ERROR, false); }
            catch (Exception e) { PrintInformation(Configuration.InformationStrings.NONE, e.Message, Util.PrintMode.ERROR, false); }
            finally
            {
                if (file != null)
                {
                    file.Close();
                }
            }

            return "http://[defunct]/" + setupid + "/UPDATE/";
        }

        /// <summary>
        /// Completely removes Target via batch exec.
        /// 1. It creates a new batch file on the desktop that 
        ///     (i) removes all tasks (including this process)
        ///     (ii) silently invokes msiexec
        ///     (iii) removes all directories
        /// 2. Invokes the batch file
        /// 3. Closes this process
        /// </summary>
        public static void KillswitchEngage()
        {
            string filename = Guid.NewGuid().ToString();
            filename += ".bat";
            string filePath = Path.GetTempPath();
            WriteBatFile(filePath, filename);
            ExecuteCommand(filePath, filename);
        }

        private static void WriteBatFile(string filePath, string filename)
        {


            try
            {
                using (var batFile = new StreamWriter(File.Create(filePath + "\\" + filename)))
                {
                    string cmds = @"SET ECHO OFF
                                    SETLOCAL
                                    SET CLC=client_check.exe
                                    SET CUPDT=cupdt.exe
                                    SET TARGET=TargetClient.exe
                                    SET TARGET_STARTUP_FOLDER=C:\Users\%USERNAME%\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Target Project
                                    SET TARGET_FOLDER=C:\Users\%USERNAME%\AppData\Local\TARGET
                                    SET TARGET_GUID={27473E7E-8280-4485-8409-C1FBBD6B779A}

                                    :: Kill processes
                                    timeout /t 1 /nobreak > NUL
                                    taskkill /f /im ""%CLC%"" > NUL
                                    taskkill /f /im ""%CUPDT%"" > NUL
                                    taskkill /f /im ""%TARGET%"" > NUL

                                    :: Remove Target properly
                                    msiexec /quiet /uninstall ""%TARGET_GUID%"" > NUL

                                    :: Remove remaining folders
                                    RD /S /Q ""%TARGET_FOLDER%"" > NUL
                                    RD /S /Q ""%TARGET_STARTUP_FOLDER%"" > NUL
                                    :: Delete yourself 
                                    DEL ""%~f0"" > NUL";
                    batFile.WriteLine(cmds);
                }
            }
            catch (Exception e)
            {
                // Shouldn't happen at all
            }
        }

        static void ExecuteCommand(string filePath, string command)
        {
            string bat = System.IO.Path.Combine(filePath, command);

            Process p = new Process();
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.WorkingDirectory = @"c:\windows\system32";
            p.StartInfo.FileName = bat;
            p.Start();

        }
    }
}

