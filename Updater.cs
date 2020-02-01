// TESTING
//#define __DEBUG
//#define __TEST_PATH           ///< Use this to test the updater with test path
#define __RELEASE               ///< Release version see below
//#define __QUICKTIMER          ///< Set timer to a small value for test purposes
#define __NORMALTIMER           ///< The normal timer (QUICKTIMER and NORMALTIMER are exclusive)
//#define __READSERVER          ///< A debug control: reads server from file in current folder (debug.0)


#if __DEBUG                         ///< In Debug mode we may still use fine grained settings
#define __TEST_BINARY               ///< Run with binary updates enabled
#define __TEST_CONFIGURATION        ///< Run with configuration file enabled
#define __TEST_LOCALVERSION         ///< Run with "new local file" enabled
#define __START_UPDATE_PROCESS      ///< Enable a new update process to be started
#define __START_TARGET_PROCESS       ///< Enable a new target process to be started

#endif

#if __RELEASE
#define __TEST_KILL                 ///< Delete Target permanently
#define __TEST_BINARY               ///< Run with binary updates enabled
#define __TEST_CONFIGURATION        ///< Run with configuration file enabled
#define __TEST_LOCALVERSION         ///< Run with "new local file" enabled
#define __START_UPDATE_PROCESS      ///< Enable a new update process to be started
#define __START_TARGET_PROCESS       ///< Enable a new target process to be started
#endif


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.IO.Compression;
using System.Threading;
using System.Diagnostics;

namespace Updater
{
    /// <summary>
    /// Updates or deletes target  based on a version file online on the location:
    /// http://servername/uuid/UPDATE/v.0
    /// The version file has a simple grammar, consisting of comments:
    /// # I'm a comment
    /// A version number for the target client:
    /// bin x.y.z.w.
    /// A version number for the client_check file:
    /// conf x.y.z.w
    /// And finally the optional kill command:
    /// kill
    /// </summary>
    public class UpdaterMain
    {
        /// <summary>
        /// Provides Return Codes for CheckUpdate
        /// </summary>
        enum UPDATE_STATUS
        {
            ONLINE_ERROR = -2,      ///< If something goes wrong online
            OFFLINE_ERROR = -1,     ///< If something goes wrong locally (e.g. permission problem)
            START_UPDT = 0,         ///< Update process can be started (i.e. cupdt.exe)
            START_TARGET = 1,        ///< Target can be started (i.e. targetclient.exe)
            KILLSWITCH = 2          ///< Remove Target completely and of course silently
        };

        static readonly int RETRIES = 1000;                                     ///< How often do we retry until we stop cupdt
#if __QUICKTIMER
        static readonly int UPDATE_TIMER = 10 * 1000;                           ///< 1 hour to next try
        static readonly int COOL_DOWN_FACTOR = 2;                               ///< A day. Needed if something goes wrong on the server
#endif

#if __NORMALTIMER
        static int UPDATE_TIMER = 1800*1000;                                    ///< 1 hour to next try
        static int COOL_DOWN_FACTOR = 2;                                        ///< A day. Needed if something goes wrong on the server
#endif
        static Mutex MUTEX;
        static readonly string MUTEXSTRING = "e988d278-8fe7-4f05-b017-5a3a91366e82";
        static readonly string VERSION_FILENAME = "v.0";                                ///< version file online
        static readonly string SETUP_ID = "57ee1e2e-5f0d-11e6-8f35-0a9e736af3a5";       ///< uniquely define where this updater comes from                                                                    ///
        static string SERVER = "http://[defunct]/"+SETUP_ID+"/UPDATE/";                 ///< update location
        static readonly string SETUP_NAME_BASE = "TARGET_";                             ///< setup base, TARGET_x.y.z.w
        static readonly string SETUP_FILEEXTENSION = ".zip";                            ///< setup file extension
        static readonly string TARGET_BINARY_NAME = "targetclient.exe";    ///< binary name of target (if changed by user, won't work anymore)
        static readonly string TARGET_UPDT_NAME = "cupdt.exe";             ///< binary name of target update tool
        static string EXECUTABLE_NAME;                                     ///< real name of the executable
        static string EXECUTABLE_PATH;                                     ///< path of the executable (path TO executable)
        static string EXECUTABLE_DIRECTORY;                                ///< directory of the executable
        static string EXECUTABLE_DIRECTORY_PARENT;                         ///< parent of executable's directory
        static readonly string TARGET_REGISTRY_NAME = "TARGET";            ///< target data is also stored in registry (autostart)
        static readonly string TARGET_CONFIG_BASE = "client_check_";               ///< xml config file for the detection tool
        
        // Variables to kill processes by name
        static readonly string TARGET_KILL_NAME = "targetclient";          ///< target data is also stored in registry (autostart)
        static readonly string KILL_NAME = "client_check";                         ///< xml config file for the detection tool

        static string BINARY_LOCAL_VERSION_STRING;                         ///< file version of the current installed targetclient binary
        static string CONFIG_LOCAL_VERSION_STRING;                         ///< version of the configuration file in use client_check_x.y.z.w

        static readonly string NUMERIC_VERSION_PATTERN = @"^([0-9]+\.){3}[0-9]*$";                      ///< version pattern x.y.z.w
        static readonly string CONFIG_LOCAL_VERSION_STRING_PATTERN = @"client_check_([0-9]+\.){3}[0-9]*";      ///< client_check_x.y.z.w pattern

        static readonly string[] MIGRATION_FILES = { "user.dat" };                  ///< files that need to be ated
        static readonly string LINE = "--------------------------------------------------------------------";


        /// <summary>
        /// Finds an update for target. This is a bootstrapper, meaning that cupdt.exe is started before targetclient is started. 
        /// In a Nutshell:
        /// - If a new local version is found in the parent folder i.e. EXECUTABLE_DIRECTORY_PARENT/x.y.z.w/targetclient.exe then cupdt.exe of that folder is started
        /// - If a new online version (x.y.z.w) exists it is downloaded and extracted in EXECUTABLE_DIRECTORY_PARENT/x.y.z.w/
        /// - If a new config version (client_check_x.y.z.w) exists it is downloaded to the currenct folder, i.e. EXECUTABLE_DIRECTORY
        /// 
        /// .. the rest of the code are various checks to prevent infinite update loops etc.
        /// </summary>
        /// <param name="args">None</param>
        static void Main(string[] args)
        {
           // Determine whether we are in the correct folder, and set static vars
            Init();
            EnactMutex();

            int updateTimer = UPDATE_TIMER;
            string newVersionPath;
            while (true)
            {
                bool exit = false;
                if (updateTimer >= (RETRIES * COOL_DOWN_FACTOR * UPDATE_TIMER))
                    exit = true;

                UPDATE_STATUS retUpdate = CheckUpdate(out newVersionPath);

                switch (retUpdate)
                {
                    // If we were not able to download the file (server or access rights)
                    case UPDATE_STATUS.ONLINE_ERROR:
                        Util.PrintInformation(Configuration.InformationStrings.NONE, "Update was not successful, (probably) server error, incrementing cooldown", Util.PrintMode.ERROR, exit);
                        updateTimer *= COOL_DOWN_FACTOR;
                        break;
                    case UPDATE_STATUS.OFFLINE_ERROR:
                        Util.PrintInformation(Configuration.InformationStrings.NONE, "Update was not successful, (probably) client error, incrementing cooldown", Util.PrintMode.ERROR, exit);
                        updateTimer *= COOL_DOWN_FACTOR;
                        break;
                    case UPDATE_STATUS.START_UPDT:
                        Util.PrintInformation(Configuration.InformationStrings.NONE, "Update was successful: "+newVersionPath, Util.PrintMode.NORMAL, false);
                        if (!StartNewUpdtProc(newVersionPath))
                        {
                            Util.PrintInformation(Configuration.InformationStrings.NONE, "Could not start new updater", Util.PrintMode.ERROR, exit);
                            updateTimer *= COOL_DOWN_FACTOR;
                            break;
                        }
#if __DEBUG
                        Console.ReadKey();
#endif
                        Util.PrintInformation(Configuration.InformationStrings.NONE, "Successfully started new updater", Util.PrintMode.NORMAL, true);
                        break;
                    case UPDATE_STATUS.START_TARGET:
                        // If no more updates were conducted set current cupdt.exe as starting point in registry 
                        Util.CreateAutostartEntry(TARGET_REGISTRY_NAME, Path.Combine(EXECUTABLE_DIRECTORY, TARGET_UPDT_NAME));
                        // Start current (which is the newest) target
                        if (!StartNewTargetProc(EXECUTABLE_DIRECTORY))
                        {
                            Util.PrintInformation(Configuration.InformationStrings.NONE, "Could not start new target", Util.PrintMode.ERROR, exit);
                            updateTimer *= COOL_DOWN_FACTOR;
                            break;
                        }
                        Util.PrintInformation(Configuration.InformationStrings.NONE, "Successfully started new target version", Util.PrintMode.NORMAL, false);
                    
                        break;
                    case UPDATE_STATUS.KILLSWITCH:

                        if (!Util.RemoveAutostartEntry(TARGET_REGISTRY_NAME, Path.Combine(EXECUTABLE_DIRECTORY, TARGET_UPDT_NAME)))
                        {
                            Util.PrintInformation(Configuration.InformationStrings.NONE, "Target killswitch did not succeed", Util.PrintMode.ERROR, false);
                        }
                        Util.KillswitchEngage();
                        break;
                    
                    // Some unknown error occurred and we immediately leave the update application
                    default: 
                        Util.PrintInformation(Configuration.InformationStrings.NONE, "Unknown error occurred", Util.PrintMode.ERROR, true);
                        break;
                }

                // Wait until updateTimer was completed (usually an hour)
                Thread.Sleep(updateTimer);
            }
        }

        public static void Init()
        {
            try
            {
                AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
                AppDomain.CurrentDomain.ProcessExit += ProcessExitEvent;


                EXECUTABLE_NAME = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
                EXECUTABLE_PATH = System.Reflection.Assembly.GetExecutingAssembly().Location;
                EXECUTABLE_DIRECTORY = System.IO.Path.GetDirectoryName(EXECUTABLE_PATH);
                EXECUTABLE_DIRECTORY_PARENT = System.IO.Directory.GetParent(EXECUTABLE_DIRECTORY).FullName;
                CONFIG_LOCAL_VERSION_STRING = GetConfigVersionString();

                // Init logger before using it
                Logger.Logger.CreateLogger(EXECUTABLE_DIRECTORY);
                Util.PrintInformation(Configuration.InformationStrings.NONE, "Initializing", Util.PrintMode.NORMAL, false);

                if (!File.Exists(Path.Combine(EXECUTABLE_DIRECTORY, TARGET_BINARY_NAME)))
                    Util.PrintInformation(Configuration.InformationStrings.NONE, Path.Combine(EXECUTABLE_DIRECTORY, TARGET_BINARY_NAME) + " has been moved or target deleted", Util.PrintMode.ERROR, true);

                BINARY_LOCAL_VERSION_STRING = FileVersionInfo.GetVersionInfo(Path.Combine(EXECUTABLE_DIRECTORY, TARGET_BINARY_NAME)).FileVersion;

#if __READSERVER
                SERVER = Util.GetDebugServer(EXECUTABLE_DIRECTORY, SETUP_ID);
#endif

                PrintStaticVariables();
            }
            catch (Exception e)
            {
                if (e is ArgumentException)
                    Util.PrintInformation(Configuration.InformationStrings.NONE, "Path.Combine(1): " + EXECUTABLE_DIRECTORY + " " + TARGET_BINARY_NAME, Util.PrintMode.ERROR, false);
                if (e is ArgumentNullException)
                    Util.PrintInformation(Configuration.InformationStrings.NONE, "Path.Combine(2): " + EXECUTABLE_DIRECTORY + " " + TARGET_BINARY_NAME, Util.PrintMode.ERROR, false);

                if (e is FileNotFoundException)
                    Util.PrintInformation(Configuration.InformationStrings.NONE, "GetVersionInfo: File not found", Util.PrintMode.ERROR, false);
                if (e is NullReferenceException)
                    PrintStaticVariables();
                Util.PrintInformation(Configuration.InformationStrings.NONE, "Update has been moved or target deleted", Util.PrintMode.ERROR, true);


            }
        }

        private static void ProcessExitEvent(object sender, EventArgs e)
        {
            Util.PrintInformation(Configuration.InformationStrings.NONE, "Received termination signal", Util.PrintMode.NORMAL, false);
            Util.PrintInformation(Configuration.InformationStrings.NONE, "Trying to release mutex", Util.PrintMode.NORMAL, false);

            if (!DisposeMutex())
                Util.PrintInformation(Configuration.InformationStrings.NONE, "Unable to release mutex", Util.PrintMode.ERROR, true);
            
            Util.PrintInformation(Configuration.InformationStrings.NONE, "Successfully released mutex", Util.PrintMode.NORMAL, true);
        }

        private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            Util.PrintInformation(Configuration.InformationStrings.NONE, "Caught unhandled exception.", Util.PrintMode.ERROR, true);
        }


        /// <summary>
        /// Most important method. Periodically checks for updates in local folders and online, for both new binaries and config files.
        /// </summary>
        /// <returns>
        /// -2 on online error (i.e. version not found), 
        /// -1 on offline error (internal error on host, exception etc.), 
        /// 0 no error (start update proc), 
        /// 1 no error (start target proc, current version is the newest)
        /// </returns>
        private static UPDATE_STATUS CheckUpdate(out string newVersionPath)
        {
            ////////////////////////////////////////////////////////////////
            // Begin finding local new version
            //
            // Find a local version newer than the current one and start it
            // Read directory and find folder x.y.z.w > this version x.y.z.w
            ////////////////////////////////////////////////////////////////

#if __TEST_LOCALVERSION
            newVersionPath = FindNewLocalVersion();
#endif

#if __DEBUG
            Console.ReadKey();
#endif

            // if no binary was returned there is probably no new version 
            if (String.IsNullOrEmpty(newVersionPath))
                Util.PrintInformation(Configuration.InformationStrings.NONE, "No new local version\n" + LINE, Util.PrintMode.NORMAL, false);

            // if there is a new version
            else
                return UPDATE_STATUS.START_UPDT; // this updater is not needed anymore

            ////////////////////////////////////////////////////////////////////////////
            // Begin searching for new online version 
            //
            // Download version file
            // Do pattern matching on:  bin x.y.z.w
            // Do pattern matching on:  conf x.y.z.w
            // Install new version if remote version x.y.z.w is newer than local version
            /////////////////////////////////////////////////////////////////////////////

            string tmpFileVersion = Path.Combine(EXECUTABLE_DIRECTORY, VERSION_FILENAME);
            string downloadLinkVersionFile = SERVER + VERSION_FILENAME;                     // Download link for the version file
            string remoteVersion = null;                                                    // Will hold the version string of the new binary package or client_check.xml
            string newExecutableDirectory = EXECUTABLE_DIRECTORY;                           // The new directory in which our new executable should reside (or the current one)
            bool BinaryUpdate = false;
            bool ConfigUpdate = false;
            bool KillUpdate = false;

            string downloadLink = downloadLinkVersionFile;
            string tmpFile = tmpFileVersion;
            string checkDirectory = null;                                                            // Temp Variable to store a potential new directory name
            
            if (!Util.DownloadFile(downloadLink, tmpFile))
                return UPDATE_STATUS.ONLINE_ERROR;
            
            //////////////////////////////////////////7////////////////////
            // Begin Update Process
            ///////////////////////////////////////////////////////////////

            // KILL
            // Check whether the kill command was given to us
#if __TEST_KILL
            remoteVersion = FindNewOnlineVersion(tmpFile, Configuration.VersionMode.KILL);
#endif
#if __DEBUG
            Console.ReadKey();
#endif

            if (!String.IsNullOrEmpty(remoteVersion))
            {
                return UPDATE_STATUS.KILLSWITCH;
            }


#if __DEBUG
            Console.ReadKey();
#endif

            // BINARY
            // Check whether version for binary from the server is newer than local one
#if __TEST_BINARY
            remoteVersion = FindNewOnlineVersion(tmpFile, Configuration.VersionMode.BINARY);
#endif
#if __DEBUG
            Console.ReadKey();
#endif

            if (!String.IsNullOrEmpty(remoteVersion))
            {
                Util.PrintInformation(Configuration.InformationStrings.INFORMATION_000_004, ": BINARY", Util.PrintMode.NORMAL, false);
                // remember that an update exists
                // install the binary in the corresponding directory
                checkDirectory = InstallUpdate(SETUP_NAME_BASE, SETUP_FILEEXTENSION, remoteVersion, EXECUTABLE_DIRECTORY, Configuration.VersionMode.BINARY);
                // if the update was applied successfully migrate user centric data
                if (!String.IsNullOrEmpty(checkDirectory))
                {
                    newExecutableDirectory = checkDirectory;
                    BinaryUpdate = true;
                    MigrateUserData(newExecutableDirectory, EXECUTABLE_DIRECTORY);
                    Util.PrintInformation(Configuration.InformationStrings.NONE, "Binary update completed.", Util.PrintMode.NORMAL, false);
                }
                remoteVersion = null;
            }


#if __DEBUG
            Console.ReadKey();
#endif


            // CONFIGURATION
            // Check whether config version from the server is new, i.e. client_check_x.y.z.w and store it in newExecutableDirectory (which is either the new or the old one)
#if __TEST_CONFIGURATION
            remoteVersion = FindNewOnlineVersion(tmpFileVersion, Configuration.VersionMode.CONFIGURATION);
#endif
#if __DEBUG
            Console.ReadKey();
#endif

            if (!String.IsNullOrEmpty(remoteVersion))
            {
                Util.PrintInformation(Configuration.InformationStrings.INFORMATION_000_004, ": CONFIGURATION", Util.PrintMode.NORMAL, false);
                // install the configuration in the corresponding directory (new executable directory)
                checkDirectory = InstallUpdate(TARGET_CONFIG_BASE, "", remoteVersion, newExecutableDirectory, Configuration.VersionMode.CONFIGURATION);

                if (!String.IsNullOrEmpty(checkDirectory))
                {
                    ConfigUpdate = true;
                    Util.PrintInformation(Configuration.InformationStrings.NONE, "Configuration update completed.", Util.PrintMode.NORMAL, false);
                }
            }

#if __DEBUG
            Console.ReadKey();
#endif

            ////////////////////////////////////////////////////////////////
            // Set autostart to new version and (finally) start target
            ////////////////////////////////////////////////////////////////

            // If all updates were conducted successfully we can start the new updater and exit this updater
            if (BinaryUpdate || ConfigUpdate)
            {
                newVersionPath = newExecutableDirectory;
                return UPDATE_STATUS.START_UPDT;
            }

            else
            {
                return UPDATE_STATUS.START_TARGET;
            }

#if __DEBUG
            Console.ReadKey();
#endif

        }

        /// <summary>
        /// Retrieve the version string from client_check_x.y.z.w -> x.y.z.w
        /// </summary>
        /// <returns>Null if there is no such file (or path does not exist)</returns>
        public static string GetConfigVersionString()
        {
            return Util.ExtractVersionStringName(EXECUTABLE_DIRECTORY, CONFIG_LOCAL_VERSION_STRING_PATTERN);
        }


        /// <summary>
        /// Migrates various user files to the new directoy. For now only user.xml
        /// </summary>
        /// <param name="toPath">The directory to which we copy the files</param>
        /// <param name="fromPath">The directory from which we copy the files</param>
        public static void MigrateUserData(string toPath, string fromPath)
        {
            if (String.IsNullOrEmpty(toPath) || String.IsNullOrEmpty(fromPath))
            {
                Util.PrintInformation(Configuration.InformationStrings.NONE, "Can't migrate data, path error", Util.PrintMode.ERROR, false);
                return;
            }
            
            foreach (String s in MIGRATION_FILES)
                Util.CopyFile(Path.Combine(fromPath, s), Path.Combine(toPath, s), true);
        }

        public static bool StartNewUpdtProc(string newExecutableDirectory)
        {
            if (String.IsNullOrEmpty(newExecutableDirectory))
            {
                Util.PrintInformation(Configuration.InformationStrings.NONE, "Can't execute process, no executbale directory", Util.PrintMode.ERROR, false);
                return false;
            }
            Util.PrintInformation(Configuration.InformationStrings.NONE, "NEW UPDATE PROCESS: " + Path.Combine(newExecutableDirectory, TARGET_UPDT_NAME), Util.PrintMode.NORMAL, false);
            // crucial, we remove the old entry of the tool from the registry
            Util.RemoveAutostartEntry(TARGET_REGISTRY_NAME, EXECUTABLE_PATH);
            
            // Starting new Target Process
            string ProcessToStart = Path.Combine(newExecutableDirectory, TARGET_UPDT_NAME);

#if __START_UPDATE_PROCESS

            if (!DisposeMutex())
                Util.PrintInformation(Configuration.InformationStrings.NONE, "Unable to release mutex", Util.PrintMode.ERROR, false);

            Util.PrintInformation(Configuration.InformationStrings.NONE, "Successfully released mutex", Util.PrintMode.NORMAL, false);
       
            return Util.StartBinary(ProcessToStart);                
#else
            return true;
#endif
        }



        public static bool StartNewTargetProc(string newExecutableDirectory)
        {
            // Closing old Target Process
            Util.KillProcesses(TARGET_KILL_NAME);
            Util.KillProcesses(KILL_NAME);

            // Starting new Target Process
            string ProcessToStart = Path.Combine(newExecutableDirectory, TARGET_BINARY_NAME);
#if __START_TARGET_PROCESS
            return Util.StartBinary(ProcessToStart);                
#else
            return true;
#endif           
        }

        /// <summary>
        /// Finds new version of Target application by traversing all folders x.y.z.w comparing BINARY_LOCAL_VERSION_STRING
        /// with folder names. If the largest version folder is found, the target binary file version is compared with the current binary
        /// </summary>
        /// <returns>Full path to the binary that can be executed or null</returns>
        public static string FindNewLocalVersion()
        {
            string CurrentVersion = BINARY_LOCAL_VERSION_STRING;    // Hold the current version of targetclient.exe
            string NewestVersion = BINARY_LOCAL_VERSION_STRING;     // Same here, but later holds the highest version on the machine

            Util.PrintInformation(Configuration.InformationStrings.NONE, "Current Version: " + NewestVersion, Util.PrintMode.NORMAL, false);
            
            // Get all directory names for %appdata%/TARGET/Target
            string[] entries = Util.GetDirectoryEntries(EXECUTABLE_DIRECTORY_PARENT);

            System.Text.RegularExpressions.Regex re = new System.Text.RegularExpressions.Regex(NUMERIC_VERSION_PATTERN);

            // No directories? Unexpected and we return
            if (entries == null)
            {
                Util.PrintInformation(Configuration.InformationStrings.NONE, "No directory entries to show", Util.PrintMode.ERROR, false);
                return null;
            }

            // For each directory determine whether the version string is newer or not (gets us the highest version x.y.z.w directory)
            foreach (string entry in entries)
            {
                if (re.IsMatch(entry))
                {
                    Util.PrintInformation(Configuration.InformationStrings.NONE,"Directory Name: "+entry, Util.PrintMode.NORMAL,false);
                    int comp = Util.CompareVersion(NewestVersion, entry);

                    switch (comp)
                    {
                        case 0:
                            Util.PrintInformation(Configuration.InformationStrings.NONE, "Same version", Util.PrintMode.NORMAL, false);
                            break;
                        case 1:
                            Util.PrintInformation(Configuration.InformationStrings.NONE, "Current version is newer", Util.PrintMode.NORMAL, false);
                            break;
                        case 2:
                            Util.PrintInformation(Configuration.InformationStrings.NONE, "New Version Found", Util.PrintMode.NORMAL, false);
                            NewestVersion = entry;
                            break;
                        default:
                            Util.PrintInformation(Configuration.InformationStrings.NONE, "Version strings seem to be corrupted", Util.PrintMode.ERROR, false);
                            return null;
                    }
                }
            }

            // Create path to binary
            string Folder = Path.Combine(EXECUTABLE_DIRECTORY_PARENT, NewestVersion);
            string PathToBinary = Path.Combine(Folder, TARGET_BINARY_NAME);

            // As a last test, determine whether the binary is indeed newer than the local version
            int vcomp = Util.CompareBinaryFileVersion(PathToBinary, BINARY_LOCAL_VERSION_STRING);

            // If it is, return the path to the folder
            if (vcomp == 1)
                return Folder;
            return null;
        }

        /// <summary>
        /// Compare various kinds of version strings, or find kill string.
        /// </summary>
        /// <param name="tmpFileVersion">Path to file that needs to be parsed for a version string</param>
        /// <param name="vmode">What kind of update, config files or binary, or killswitch</param>
        /// <returns></returns>
        public static string FindNewOnlineVersion(string tmpFileVersion, Configuration.VersionMode vmode)
        {
            Util.PrintInformation(Configuration.InformationStrings.NONE, "\n"+LINE, Util.PrintMode.NORMAL, false);

            if (String.IsNullOrEmpty(tmpFileVersion))
            {
                Util.PrintInformation(Configuration.InformationStrings.NONE, "tmpFileVersion not set", Util.PrintMode.ERROR, false);
                return null;
            }

            string remoteVersionString = Util.ExtractVersionStringFile(tmpFileVersion, vmode);

            if (String.IsNullOrEmpty(remoteVersionString))
            {
                Util.PrintInformation(Configuration.InformationStrings.NONE, "Remote Version String could not be extracted", Util.PrintMode.ERROR, false);
                return null;
            }

            Util.PrintInformation(Configuration.InformationStrings.NONE, "REMOTEVERSION:"+ remoteVersionString, Util.PrintMode.NORMAL, false);


            int comp = -1; 
            switch (vmode)
            { 
                case Configuration.VersionMode.BINARY:
                    Util.PrintInformation(Configuration.InformationStrings.NONE, "BINARY_LOCAL_VERSION_STRING:" + BINARY_LOCAL_VERSION_STRING, Util.PrintMode.NORMAL, false);
                    comp = Util.CompareVersion(remoteVersionString, BINARY_LOCAL_VERSION_STRING);
                    break;
                case Configuration.VersionMode.CONFIGURATION:
                    Util.PrintInformation(Configuration.InformationStrings.NONE, "CONFIG_LOCAL_VERSION_STRING:" + CONFIG_LOCAL_VERSION_STRING, Util.PrintMode.NORMAL, false);
                    comp = Util.CompareVersion(remoteVersionString, CONFIG_LOCAL_VERSION_STRING);
                    break;
                case Configuration.VersionMode.KILL:
                    Util.PrintInformation(Configuration.InformationStrings.NONE, "KILL_MODE", Util.PrintMode.NORMAL, false);
                    return remoteVersionString;
                default:
                    Util.PrintInformation(Configuration.InformationStrings.NONE, "Unsupported vmode", Util.PrintMode.ERROR, false);
                    return null;
            }
            
            // Assuming the remote version string is newer than the other, we return that version string, e.g.
            // 1.2.3.4 > 0.0.2.1
            switch (comp)
            {
                case 0:
                    Util.PrintInformation(Configuration.InformationStrings.NONE, "Same version", Util.PrintMode.NORMAL, false);
                    return null;
                case 1:
                    Util.PrintInformation(Configuration.InformationStrings.NONE, "Remote version is newer", Util.PrintMode.NORMAL, false);
                    return remoteVersionString;
                case 2:
                    Util.PrintInformation(Configuration.InformationStrings.NONE, "Local version is newer", Util.PrintMode.NORMAL, false);
                    return null;
                default:
                  Util.PrintInformation(Configuration.InformationStrings.INFORMATION_000_002, "", Util.PrintMode.ERROR, false);
                    return null;
            }
        }

        /// <summary>
        /// Installs the new version of the binary and update tool, together with libraries, in parent folder with corresponding version signature.
        /// If C:\Program Files (x86)\TARGET\Target Project is the current directory, the new version will be installed to 
        /// C:\Programs (x86)\TARGET\x.y.z.w\
        /// 
        /// In case of a binary update InstallUpdate compares the new binary first, before install 
        /// Then the old program will be closed and the new one will be started.
        /// 
        /// If it doesn't succeed don't invoke any error handling
        /// </summary>
        /// <param name="fileName">Filename to be downloaded</param>
        /// <param name="fileExtension">Different downloads have different file extensions, some have none ""</param>
        /// <param name="remoteVersionString">Version string that specifies file to download</param>
        /// <param name="path">Path to the current working directory</param>
        /// <param name="vmode">Configuration or Binary update</param>
        /// <returns>null on error otherwise new executable path</returns>
        public static string InstallUpdate(string fileName, string fileExtension, string remoteVersionString, string path, Configuration.VersionMode vmode)
        {

            if (String.IsNullOrEmpty(fileName) || String.IsNullOrEmpty(remoteVersionString) || String.IsNullOrEmpty(path))
            {
                Util.PrintInformation(Configuration.InformationStrings.NONE, "Variables not set", Util.PrintMode.ERROR, false);
                return null;
            }

            fileName += remoteVersionString + fileExtension;
            string downloadLocation = Path.Combine(path, fileName);
            string downloadLink = SERVER + fileName;
            string returnPath = path;

            Util.PrintInformation(Configuration.InformationStrings.NONE, "Download Location:"+downloadLocation, Util.PrintMode.NORMAL, false);
            Util.PrintInformation(Configuration.InformationStrings.NONE, "Download Link:"+downloadLink, Util.PrintMode.NORMAL, false);

            // Download installation file (TARGET_x.y.z.w.zip or client_check_x.y.z.w)
            if (!Util.DownloadFile(downloadLink, downloadLocation))
            {
                Util.PrintInformation(Configuration.InformationStrings.NONE, "The file: "+downloadLink+" could not be downloaded", Util.PrintMode.ERROR, false);
                return null;
            }
            switch (vmode)
            {
            
                // If a binary is installed we check the binary file version to see if it is indeed newer than the current one
                case Configuration.VersionMode.BINARY:
                    string unpackLocation = Path.Combine(EXECUTABLE_DIRECTORY_PARENT, remoteVersionString);
                    string tmpFolder = Path.GetTempPath();
                    string tmpTargetFolder = Path.Combine(tmpFolder, remoteVersionString);
                    Util.PrintInformation(Configuration.InformationStrings.NONE, "Trying to extract to temp folder: " + tmpTargetFolder, Util.PrintMode.NORMAL, false);

                    bool ret = Util.ExtractFiles(downloadLocation, tmpTargetFolder);

                    // If we were able to extract the files ..
                    if (ret)
                    {
                        Util.PrintInformation(Configuration.InformationStrings.NONE, "Set unpack location: " + unpackLocation, Util.PrintMode.NORMAL, false);
                        returnPath = unpackLocation;

                        // .. create path to binary
                        string PathToBinary = Path.Combine(tmpTargetFolder, TARGET_BINARY_NAME);
                        int comp = Util.CompareBinaryFileVersion(PathToBinary, BINARY_LOCAL_VERSION_STRING);

                        // .. and test whether the new version is really newer
                        if (comp == 1)
                            Util.PrintInformation(Configuration.InformationStrings.NONE, "Binary pointed by path is newer than " + BINARY_LOCAL_VERSION_STRING, Util.PrintMode.NORMAL, false);
                        else
                        {
                            Util.PrintInformation(Configuration.InformationStrings.NONE, "Binary pointed by path is older/same than " + BINARY_LOCAL_VERSION_STRING, Util.PrintMode.NORMAL, false);
                            return null;
                        }
                    }

                    Util.ExtractFiles(downloadLocation, unpackLocation);
                    Util.PrintInformation(Configuration.InformationStrings.INFORMATION_000_006, unpackLocation, Util.PrintMode.NORMAL, false);
                    break;
                case Configuration.VersionMode.CONFIGURATION:
                    Util.PrintInformation(Configuration.InformationStrings.NONE, "Nothing to do in CONFIGURATION mode", Util.PrintMode.NORMAL, false);
                    break;
                default:
                    return null;
            }
            
            
#if __DEBUG
            Util.PrintFileSystemEntries(returnPath);
#endif

            return returnPath;
        }

        private static bool DisposeMutex()
        {
            bool removeSuccess = false;
            try
            {
                Util.PrintInformation(Configuration.InformationStrings.NONE, "Trying to release mutex ", Util.PrintMode.NORMAL, false);
                MUTEX.Close();

                try
                {
                    // Open the mutex with (MutexRights.Synchronize |
                    // MutexRights.Modify), to enter and release the
                    // named mutex.
                    //
                    MUTEX = Mutex.OpenExisting(MUTEXSTRING);
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    Util.PrintInformation(Configuration.InformationStrings.NONE, "Mutex does not seem to exist ", Util.PrintMode.NORMAL, false);
                    removeSuccess = true;
                }
                catch (UnauthorizedAccessException)
                {
                    Util.PrintInformation(Configuration.InformationStrings.NONE, "Unauthorized access to mutex ", Util.PrintMode.ERROR, false);
                    removeSuccess = false;
                }
            }
            catch (ApplicationException)
            {
                Util.PrintInformation(Configuration.InformationStrings.NONE, "Unable to dispose mutex ", Util.PrintMode.ERROR, false);
            }

            return removeSuccess;
        }
        public static void EnactMutex()
        {
            Thread.Sleep(2000); // Give the old process time to exit (although we also formally check mutex deletion)
            // Prevent multiple instances of the same application
                        bool createdNew;
                        try
                        {
                            MUTEX = new Mutex(true, MUTEXSTRING, out createdNew);
                            if (!createdNew)
                            {
                                Util.PrintInformation(Configuration.InformationStrings.NONE, "Application already started", Util.PrintMode.ERROR, true);
                            }
                        }
                        catch { }
        }


        private static void PrintStaticVariables()
        {
            Util.PrintInformation(Configuration.InformationStrings.NONE, "\n" + LINE, Util.PrintMode.NORMAL, false);
            Util.PrintInformation(Configuration.InformationStrings.NONE, "Timer:" + UPDATE_TIMER, Util.PrintMode.NORMAL, false);
            Util.PrintInformation(Configuration.InformationStrings.NONE, "Cool-Down Factor:" + COOL_DOWN_FACTOR, Util.PrintMode.NORMAL, false);
            Util.PrintInformation(Configuration.InformationStrings.NONE, "VERSION_FILENAME:" + VERSION_FILENAME, Util.PrintMode.NORMAL, false);
            Util.PrintInformation(Configuration.InformationStrings.NONE, "SERVER:" + SERVER, Util.PrintMode.NORMAL, false);
            Util.PrintInformation(Configuration.InformationStrings.NONE, "SETUP_NAME_BASE:" + SETUP_NAME_BASE, Util.PrintMode.NORMAL, false);
            Util.PrintInformation(Configuration.InformationStrings.NONE, "SETUP_FILEEXTENSION:" + SETUP_FILEEXTENSION, Util.PrintMode.NORMAL, false);
            Util.PrintInformation(Configuration.InformationStrings.NONE, "TARGET_BINARY_NAME:" + TARGET_BINARY_NAME, Util.PrintMode.NORMAL, false);
            Util.PrintInformation(Configuration.InformationStrings.NONE, "TARGET_UPDT_NAME:" + TARGET_UPDT_NAME, Util.PrintMode.NORMAL, false);
            Util.PrintInformation(Configuration.InformationStrings.NONE, "EXECUTABLE_NAME:" + EXECUTABLE_NAME, Util.PrintMode.NORMAL, false);
            Util.PrintInformation(Configuration.InformationStrings.NONE, "EXECUTABLE_PATH:" + EXECUTABLE_PATH, Util.PrintMode.NORMAL, false);
            Util.PrintInformation(Configuration.InformationStrings.NONE, "EXECUTABLE_DIRECTORY:" + EXECUTABLE_DIRECTORY, Util.PrintMode.NORMAL, false);
            Util.PrintInformation(Configuration.InformationStrings.NONE, "EXECUTABLE_DIRECTORY_PARENT:" + EXECUTABLE_DIRECTORY_PARENT, Util.PrintMode.NORMAL, false);
            Util.PrintInformation(Configuration.InformationStrings.NONE, "TARGET_REGISTRY_NAME:" + TARGET_REGISTRY_NAME, Util.PrintMode.NORMAL, false);
            Util.PrintInformation(Configuration.InformationStrings.NONE, "TARGET_CONFIG_BASE:" + TARGET_CONFIG_BASE, Util.PrintMode.NORMAL, false);
            Util.PrintInformation(Configuration.InformationStrings.NONE, "BINARY_LOCAL_VERSION_STRING:" + BINARY_LOCAL_VERSION_STRING, Util.PrintMode.NORMAL, false);
            Util.PrintInformation(Configuration.InformationStrings.NONE, "CONFIG_LOCAL_VERSION_STRING:" + CONFIG_LOCAL_VERSION_STRING, Util.PrintMode.NORMAL, false);
            Util.PrintInformation(Configuration.InformationStrings.NONE, "NUMERIC_VERSION_PATTERN:" + NUMERIC_VERSION_PATTERN, Util.PrintMode.NORMAL, false);
            Util.PrintInformation(Configuration.InformationStrings.NONE, "CONFIG_LOCAL_VERSION_STRING_PATTERN:" + CONFIG_LOCAL_VERSION_STRING_PATTERN, Util.PrintMode.NORMAL, false);

            foreach (String s in MIGRATION_FILES)
                Util.PrintInformation(Configuration.InformationStrings.NONE, "Migration File:" + BINARY_LOCAL_VERSION_STRING, Util.PrintMode.NORMAL, false);

            Util.PrintInformation(Configuration.InformationStrings.NONE, "\n" + LINE, Util.PrintMode.NORMAL, false);
        }
    }
}
