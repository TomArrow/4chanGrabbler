using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Salaros.Configuration;

namespace _4chanGrabbler
{
    class ConfigParserMod : ConfigParser
    {

        public ConfigParserMod(string filename) : base(filename) { }

        public string[] getDuplicateValueArray(string section, string key)
        {
            List<string> values = new List<string>();

            if (sections.ContainsKey(section))
            {
                //IEnumerator<IConfigLine> blah = sections[section].Lines.GetEnumerator();

                foreach (IConfigLine line in sections[section].Lines)
                {
                    if (line is ConfigKeyValue<object>)
                    {
                        if ((line as ConfigKeyValue<object>).Name == key)
                        {
                            values.Add((line as ConfigKeyValue<object>).Value.ToString());
                        }
                    }

                }
            }

            return values.ToArray();
        }
    }
}
