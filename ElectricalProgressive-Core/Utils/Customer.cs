using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ElectricalProgressive.Utils
{
    public class Customer
    {
        public int Id { get; }
        public float Required { get; }
        public Dictionary<Store, float> StoreDistances { get; }
        public Dictionary<Store, float> Received { get; } = new Dictionary<Store, float>();

        // Храним магазины в отсортированном массиве для быстрого доступа
        private Store[] _orderedStores;

        public Customer(int id, float required, Dictionary<Store, float> distances)
        {
            Id = id;
            Required = required;
            StoreDistances = distances;
            UpdateOrderedStores();
        }

        /// <summary>
        /// Оставшийся объём ресурса, необходимый клиенту
        /// </summary>
        public float Remaining
        {
            get
            {
                // Ручной подсчёт суммы вместо LINQ для производительности
                float receivedSum = 0;
                foreach (var value in Received.Values)
                {
                    receivedSum += value;
                }
                return Required - receivedSum;
            }
        }

        /// <summary>
        /// Общая дистанция поставки (вычисляется по формуле)
        /// </summary>
        public double TotalDistance
        {
            get
            {
                // Ручной подсчёт вместо LINQ
                double total = 0;
                foreach (var kvp in Received)
                {
                    total += kvp.Key.DistanceTo(this) * kvp.Value;
                }
                return total;
            }
        }

        /// <summary>
        /// Обновляет порядок магазинов по расстоянию
        /// </summary>
        public void UpdateOrderedStores()
        {
            var items = StoreDistances.ToArray();
            Array.Sort(items, (a, b) => a.Value.CompareTo(b.Value));
            _orderedStores = new Store[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                _orderedStores[i] = items[i].Key;
            }
        }
        

        /// <summary>
        /// Возвращает доступные магазины в порядке увеличения расстояния
        /// </summary>
        public IEnumerable<Store> GetAvailableStores() => _orderedStores;
    }
}
