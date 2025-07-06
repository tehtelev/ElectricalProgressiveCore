using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ElectricalProgressive.Utils
{
    public class Simulation
    {
        public List<Customer> Customers { get; set; }
        public List<Store> Stores { get; set; }

        public void Run()
        {
            // Инициализация суммарного запроса для магазинов
            for (int i = 0; i < Stores.Count; i++)
            {
                Stores[i].totalRequest = 0;
            }

            bool hasActiveStores;
            bool hasPendingCustomers;

            do
            {
                // Обновление магазинов для покупателей
                for (int i = 0; i < Customers.Count; i++)
                {
                    Customers[i].UpdateOrderedStores();
                }

                // Сброс запросов магазинов
                for (int i = 0; i < Stores.Count; i++)
                {
                    Stores[i].ResetRequests();
                }

                // Обработка запросов покупателей
                for (int c = 0; c < Customers.Count; c++)
                {
                    var customer = Customers[c];
                    if (customer.Remaining <= 0.001f)
                        continue;

                    float remaining = customer.Remaining;
                    var availableStores = customer.GetAvailableStores();

                    // Используем pattern matching для массива вместо IEnumerable
                    if (availableStores is Store[] storesArray)
                    {
                        ProcessStoresArray(customer, remaining, storesArray);
                    }
                    else
                    {
                        ProcessStoresEnumerable(customer, remaining, availableStores);
                    }
                }

                // Обработка запросов в магазинах
                for (int i = 0; i < Stores.Count; i++)
                {
                    Stores[i].ProcessRequests();
                }

                // Проверка условий продолжения
                hasActiveStores = false;
                for (int i = 0; i < Stores.Count; i++)
                {
                    if (!Stores[i].ImNull)
                    {
                        hasActiveStores = true;
                        break;
                    }
                }

                hasPendingCustomers = false;
                for (int i = 0; i < Customers.Count; i++)
                {
                    if (Customers[i].Remaining > 0.001f)
                    {
                        hasPendingCustomers = true;
                        break;
                    }
                }

            } while (hasActiveStores && hasPendingCustomers);
        }

        private void ProcessStoresArray(Customer customer, float remaining, Store[] stores)
        {
            for (int s = 0; s < stores.Length; s++)
            {
                var store = stores[s];
                if (store.Stock <= 0.001f && store.ImNull)
                    continue;

                float requested = remaining;
                store.CurrentRequests[customer] = requested;
                remaining -= requested;

                if (remaining <= 0.001f)
                    break;
            }
        }

        private void ProcessStoresEnumerable(Customer customer, float remaining, IEnumerable<Store> stores)
        {
            foreach (var store in stores)
            {
                if (store.Stock <= 0.001f && store.ImNull)
                    continue;

                float requested = remaining;
                store.CurrentRequests[customer] = requested;
                remaining -= requested;

                if (remaining <= 0.001f)
                    break;
            }
        }


        /// <summary>
        /// Сбрасывает состояние симуляции, очищая магазины и клиентов.
        /// </summary>
        public void Reset()
        {
            // Сброс магазинов
            Stores.Clear();

            // Сброс клиентов
            Customers.Clear();
        }
    }
}