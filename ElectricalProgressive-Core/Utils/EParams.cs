using System;

namespace ElectricalProgressive.Utils
{
    /// <summary>
    /// Параметры проводов/приборов как участников электрической цепи
    /// </summary>
    public struct EParams : IEquatable<EParams>
    {
        public int voltage;         //напряжение
        public float maxCurrent;    //максимальный ток
        public string material;     //индекс материала
        public float resisitivity;  //удельное сопротивление
        public byte lines;          //количество линий
        public float crossArea;     //площадь поперечного сечения
        public bool burnout;        //провод сгорел
        public bool isolated;       //изолированный проводник
        public bool isolatedEnvironment; //изолированный от окружающей среды проводник

        public EParams(int voltage, float maxCurrent, string material, float resisitivity, byte lines, float crossArea, bool burnout, bool isolated, bool isolatedEnvironment)
        {
            this.voltage = voltage;
            this.maxCurrent = maxCurrent;
            this.material = material;
            this.resisitivity = resisitivity;
            this.lines = lines;
            this.crossArea = crossArea;
            this.burnout = burnout;
            this.isolated = isolated;
            this.isolatedEnvironment = isolatedEnvironment;
        }

        public EParams()
        {
            voltage = 0;
            maxCurrent = 0.0F;
            material = "";
            resisitivity = 0.0F;
            lines = 0;
            crossArea = 0.0F;
            burnout = false;
            isolated = false;
            isolatedEnvironment = true;
        }


        public bool Equals(EParams other)
        {
            return voltage == other.voltage &&
                   maxCurrent.Equals(other.maxCurrent) &&
                   material == other.material &&
                   resisitivity.Equals(other.resisitivity) &&
                   lines == other.lines &&
                   crossArea.Equals(other.crossArea) &&
                   burnout == other.burnout &&
                   isolated == other.isolated &&
                   isolatedEnvironment == other.isolatedEnvironment;
        }

        public override bool Equals(object? obj)
        {
            return obj is EParams other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + voltage;
                hash = hash * 31 + maxCurrent.GetHashCode();
                hash = hash * 31 + material.GetHashCode();
                hash = hash * 31 + resisitivity.GetHashCode();
                hash = hash * 31 + lines;
                hash = hash * 31 + crossArea.GetHashCode();
                hash = hash * 31 + burnout.GetHashCode();
                hash = hash * 31 + isolated.GetHashCode();
                hash = hash * 31 + isolatedEnvironment.GetHashCode();
                return hash;
            }
        }
    }
}