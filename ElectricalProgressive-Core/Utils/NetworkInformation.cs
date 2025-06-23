namespace ElectricalProgressive.Utils
{
    /// <summary>
    /// Сборщик информации о сети
    /// </summary>
    public class NetworkInformation
    {
        public float Consumption;
        public float Capacity;    //Емкость батарей
        public float MaxCapacity; //Максимальная емкость батарей
        public float Production;
        public float Request;
        public Facing Facing = Facing.None;
        public int NumberOfAccumulators;
        public int NumberOfBlocks;
        public int NumberOfConsumers;
        public int NumberOfProducers;
        public int NumberOfTransformators;
        public EParams eParamsInNetwork = new();
        public float current;
    }
}