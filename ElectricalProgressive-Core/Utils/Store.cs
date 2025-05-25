using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    public class Store
    {
        public int Id { get; }
        public float Stock { get; set; }
        public Dictionary<Customer, float> CurrentRequests { get; } = new Dictionary<Customer, float>();
        public bool ImNull { get; private set; }
        public Dictionary<Store, float> StoresOrders { get; } = new Dictionary<Store, float>();
        public float totalRequest;

        public Store(int id, float stock) => (Id, Stock) = (id, stock);
        public double DistanceTo(Customer customer) => customer.StoreDistances[this];

        /// <summary>
        /// Сбрасывает текущие запросы перед новым раундом распределения
        /// </summary>
        public void ResetRequests()
        {
            CurrentRequests.Clear();
        }

        /// <summary>
        /// Обрабатывает все накопленные запросы клиентов
        /// </summary>
        public void ProcessRequests()
        {
            // Ручной подсчет суммы запросов вместо LINQ
            float totalRequested = 0;
            foreach (var request in CurrentRequests)
            {
                totalRequested += request.Value;
            }

            totalRequest += totalRequested;

            // Ранний выход если нет запасов
            if (Stock <= 0.001f)
            {
                Stock = 0.0f;
                ImNull = true;
                ResetRequests();
                return;
            }

            if (totalRequested == 0) return;

            // Обработка двух сценариев: достаточно запасов или нехватка
            if (Stock >= totalRequested)
            {
                ProcessFullRequests();
            }
            else
            {
                ProcessPartialRequests(totalRequested);
            }

            // Финализация статуса магазина
            if (Stock <= 0.001f)
            {
                Stock = 0.0f;
                ImNull = true;
            }

            ResetRequests();
        }

        /// <summary>
        /// Обработка когда запасов достаточно для всех запросов
        /// </summary>
        private void ProcessFullRequests()
        {
            foreach (var request in CurrentRequests)
            {
                request.Key.Received[this] = request.Value;
                Stock -= request.Value;
            }
        }

        /// <summary>
        /// Обработка при нехватке запасов (пропорциональное распределение)
        /// </summary>
        /// <param name="totalRequested">Общая сумма запросов</param>
        private void ProcessPartialRequests(float totalRequested)
        {
            // Создаем снимок запросов для безопасной обработки
            var requests = new KeyValuePair<Customer, float>[CurrentRequests.Count];
            int i = 0;
            foreach (var request in CurrentRequests)
            {
                requests[i++] = request;
            }

            float ratio = Stock / totalRequested;
            foreach (var request in requests)
            {
                float allocated = request.Value * ratio;
                request.Key.Received[this] = allocated;
                Stock -= allocated;
            }
        }
    }
}
