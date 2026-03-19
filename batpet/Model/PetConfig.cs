using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace batpet.Model
{
    public class PetConfig
    {
        public string Name { get; set; } = "";
        public string AssetsDir { get; set; } = "";
        public int Mini1X { get; set; }
        public int Mini1Y { get; set; }
        public int Mini2X { get; set; }
        public int Mini2Y { get; set; }
        public int BackupX { get; set; }
        public int BackupY { get; set; }
        public List<string> SelectedPets { get; set; } = new();
    }

}
