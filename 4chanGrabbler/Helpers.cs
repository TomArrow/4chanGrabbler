using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace _4chanGrabbler
{
    static public class Helpers
    {
        public static string GetUnusedFilename(string baseFilename)
        {
            if (!File.Exists(baseFilename)){
                return baseFilename;
            }
            string extension = Path.GetExtension(baseFilename);

            int index = 1;
            while (File.Exists(Path.ChangeExtension(baseFilename, "." + (++index) + extension)));

            return Path.ChangeExtension(baseFilename, "." + (index) + extension);
        }
    }
}
