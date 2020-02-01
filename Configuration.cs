using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Updater
{
    public class Configuration
    {
        public enum VersionMode
        {
            BINARY,
            CONFIGURATION,
            KILL
        }
        /// <summary>
        /// Error strings
        /// They are suitable for users in form of MessageBoxes, Notification Bubbles and so on.
        /// @version 0.9
        /// </summary>
        public enum InformationStrings
        {
            [StringValue("Successfully downloaded")]
            INFORMATION_000_000,
            [StringValue("No version file found")]
            INFORMATION_000_001,
            [StringValue("Version strings seem to be corrupted")]
            INFORMATION_000_002,
            [StringValue("Remote version is newer")]
            INFORMATION_000_003,
            [StringValue("STARTING UPDATE PROCESS")]
            INFORMATION_000_004,
            [StringValue("Setup not found")]
            INFORMATION_000_005,
            [StringValue("Extracting to folder")]
            INFORMATION_000_006,
            [StringValue("Local version is newer")]
            INFORMATION_000_007,
            [StringValue("Delete files")]
            INFORMATION_000_008,
            [StringValue("Could not delete file")]
            INFORMATION_000_009,
            [StringValue("Sizes do not match")]
            INFORMATION_000_010,
            [StringValue("Path is a null reference")]
            INFORMATION_000_011,
            [StringValue("The caller does not have the required permission")]
            INFORMATION_000_012,
            [StringValue("Path is an empty string, contains only white spaces, or contains invalid characters")]
            INFORMATION_000_013,
            [StringValue("The path encapsulated in the directory object does not exist")]
            INFORMATION_000_014,
            [StringValue("Current Version")]
            INFORMATION_000_015,
            [StringValue("")]
            NONE
        }
    }
}
