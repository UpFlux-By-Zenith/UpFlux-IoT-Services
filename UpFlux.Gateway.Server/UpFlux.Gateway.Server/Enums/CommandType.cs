using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Gateway.Server.Enums
{
    /// <summary>
    /// Enumerates the types of commands that can be executed.
    /// </summary>
    public enum CommandType
    {
        /// <summary>
        /// Command to perform a rollback to a specific version.
        /// </summary>
        Rollback,

        // other command types will be added
    }
}
