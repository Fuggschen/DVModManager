// The mod manager's files are compiled into this mod (see the csproj), and they're written
// against the implicit usings its own SDK-style project turns on. Supply the ones they rely on
// so they need no changes to build here.
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
