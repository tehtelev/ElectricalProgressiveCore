﻿using Vintagestory.API.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.MathTools;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using Vintagestory.GameContent;
using static ElectricalProgressive.ElectricalProgressive;
using Vintagestory.API.Util;

[assembly: ModDependency("game", "1.20.0")]
[assembly: ModInfo(
    "Electrical Progressive: Core",
    "electricalprogressivecore",
    Website = "https://github.com/tehtelev/ElectricalProgressiveCore",
    Description = "Electrical logic library.",
    Version = "1.1.0",
    Authors = new[] { "Tehtelev", "Kotl" }
)]



namespace ElectricalProgressive
{
    public class ElectricalProgressive : ModSystem
    {
        public readonly HashSet<Network> networks = new();
        public readonly Dictionary<BlockPos, NetworkPart> parts = new(); // Хранит все элементы всех цепей

        private Dictionary<BlockPos, List<EnergyPacket>> packetsByPosition = new(); //Словарь для хранения пакетов по позициям
        private List<EnergyPacket> packetsToRemove = new (); // Список пакетов для удаления после проверки на сгорание

        private readonly List<Consumer> consumers = new();
        private readonly List<Producer> producers = new();
        private readonly List<Accumulator> accums = new();

        private readonly List<EnergyPacket> globalEnergyPackets = new(); // Глобальный список пакетов энергии

        private BlockPos[]? consumerPositions;
        private float[]? consumerRequests;
        private BlockPos[]? producerPositions;
        private float[]? producerGive;

        private BlockPos[]? consumer2Positions;
        private float[]? consumer2Requests;
        private BlockPos[]? producer2Positions;
        private float[]? producer2Give;


        private Dictionary<BlockPos, float> sumEnergy = new();


        private PathFinder pathFinder = new(); // Модуль поиска путей
        public ICoreAPI api = null!;
        private ICoreClientAPI capi = null!;
        private ICoreServerAPI sapi = null!;
        private ElectricityConfig? config;
        public static DamageManager? damageManager;
        public static WeatherSystemServer? WeatherSystemServer;

        private Simulation sim = new();
        private Simulation sim2 = new();



        public static int speedOfElectricity; // Скорость электричества в проводах (блоков в тик)
        public static bool instant; // Расчет мгновенно?
        public static int timeBeforeBurnout; // Время до сгорания проводника в секундах

        int tickTimeMs;
        private float elapsedMs = 0f;

        int envUpdater = 0;

        private long listenerId;

        /// <summary>
        /// Запуск модификации
        /// </summary>
        /// <param name="api"></param>
        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            this.api = api;

            //инициализируем обработчик уронов
            damageManager = new DamageManager(api);

            listenerId = api.Event.RegisterGameTickListener(this.OnGameTick, tickTimeMs);


        }


        /// <summary>
        /// Освобождение ресурсов
        /// </summary>
        public override void Dispose()
        {
            base.Dispose();

            // Удаляем слушатель тиков игры
            if (api != null)
            {
                api.Event.UnregisterGameTickListener(listenerId);
            }

            // Очистка глобальных коллекций и ресурсов
            consumers.Clear();
            producers.Clear();
            accums.Clear();
            globalEnergyPackets.Clear();

            consumerPositions = null;
            consumerRequests = null;
            producerPositions = null;
            producerGive = null;
            consumer2Positions = null;
            consumer2Requests = null;
            producer2Positions = null;
            producer2Give = null;

            sumEnergy.Clear();
            packetsByPosition.Clear();
            packetsToRemove.Clear();

            sim.Reset();
            sim2.Reset();

            api = null!;
            capi = null!;
            sapi = null!;
            damageManager = null;
            WeatherSystemServer = null;

            pathFinder = null!;

            networks.Clear();
            parts.Clear();

        }




        /// <summary>
        /// Запуск серверной стороны
        /// </summary>
        /// <param name="api"></param>
        public override void StartPre(ICoreAPI api)
        {
            config = api.LoadModConfig<ElectricityConfig>("ElectricityConfig.json") ?? new ElectricityConfig();
            api.StoreModConfig(config, "ElectricityConfig.json");

            speedOfElectricity = Math.Clamp(config.speedOfElectricity, 1, 16);
            instant = config.instant;
            timeBeforeBurnout = Math.Clamp(config.timeBeforeBurnout, 1, 600);

            //устанавливаем частоту просчета сети
            if (instant)
                tickTimeMs = 1000;
            else
            {
                tickTimeMs = 1000 / speedOfElectricity;
            }


        }


        /// <summary>
        /// Запуск клиентcкой стороны
        /// </summary>
        /// <param name="api"></param>
        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            this.capi = api;
            RegisterAltKeys();

        }





        /// <summary>
        /// Регистрация клавиш Alt
        /// </summary>
        private void RegisterAltKeys()
        {
            capi.Input.RegisterHotKey("AltPressForNetwork", Lang.Get("AltPressForNetworkName"), GlKeys.LAlt);
        }


        /// <summary>
        /// Серверная сторона
        /// </summary>
        /// <param name="api"></param>
        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);

            this.sapi = api;

            WeatherSystemServer = api.ModLoader.GetModSystem<WeatherSystemServer>();

        }





        /// <summary>
        /// Обновление электрической сети
        /// </summary>
        /// <param name="position"></param>
        /// <param name="facing"></param>
        /// <param name="setEparams"></param>
        /// <param name="Eparams"></param>
        /// <returns></returns>
        public bool Update(BlockPos position, Facing facing, (EParams, int) setEparams, ref EParams[] Eparams, bool isLoaded)
        {
            if (!parts.TryGetValue(position, out var part))
            {
                if (facing == Facing.None)
                    return false;
                part = parts[position] = new NetworkPart(position);
            }

            var addedConnections = ~part.Connection & facing;
            var removedConnections = part.Connection & ~facing;

            part.IsLoaded = isLoaded; 
            part.eparams = Eparams;
            part.Connection = facing;

            AddConnections(ref part, addedConnections, setEparams);
            RemoveConnections(ref part, removedConnections);

            if (part.Connection == Facing.None)
                parts.Remove(position);

            Cleaner();
            Eparams = part.eparams;
            return true;
        }


        /// <summary>
        /// Удаляем соединения
        /// </summary>
        /// <param name="position"></param>
        public void Remove(BlockPos position)
        {
            if (parts.TryGetValue(position, out var part))
            {
                parts.Remove(position);
                RemoveConnections(ref part, part.Connection);
            }
        }



        /// <summary>
        /// Чистка
        /// </summary>
        public void Cleaner()
        {
            foreach (var part in parts.Values)
            {
                //не трогать тут ничего
                if (part.eparams != null && part.eparams.Length == 6) // если проводник существует и имеет 6 проводников
                {
                    for (int i = 0; i < 6; i++)
                    {
                        if (!part.eparams[i].burnout && part.eparams[i].ticksBeforeBurnout > 0) // если проводник не сгорел и есть тики до сгорания
                            part.eparams[i].ticksBeforeBurnout--;                               // уменьшаем тики до сгорания
                    }

                }
                else
                {
                    part.eparams = new EParams[]
                    {
                            new EParams(), new EParams(), new EParams(),
                            new EParams(), new EParams(), new EParams()
                    };
                }

            }
        }


        /// <summary>
        /// Логистическая задача
        /// </summary>
        /// <param name="network"></param>
        /// <param name="consumerPositions"></param>
        /// <param name="consumerRequests"></param>
        /// <param name="producerPositions"></param>
        /// <param name="producerGive"></param>
        /// <param name="sim"></param>
        private void logisticalTask(Network network, ref BlockPos[] consumerPositions, ref float[] consumerRequests,
    ref BlockPos[] producerPositions, ref float[] producerGive, ref Simulation sim)
        {
            var cP = consumerPositions.Length; // Количество потребителей
            var pP = producerPositions.Length; // Количество производителей

            float[][] distances = new float[cP][];


            for (int i = 0; i < cP; i++)
            {
                distances[i] = new float[pP];


                for (int j = 0; j < pP; j++)
                {
                    var start = consumerPositions[i];
                    var end = producerPositions[j];
                    if (PathCacheManager.TryGet(start, end, network.version,
                            out var cachedPath,
                            out _,
                            out _,
                            out _)
                        && cachedPath != null)
                    {

                        distances[i][j] = cachedPath.Length;

                    }
                    else
                    {
                        var (path, facing, processed, usedConn) = pathFinder.FindShortestPath(start, end, network, parts);
                        if (path != null)
                        {
                            distances[i][j] = path.Length;

                            PathCacheManager.AddOrUpdate(start, end, network.version, path, facing, processed, usedConn);
                        }
                        else
                        {
                            distances[i][j] = float.MaxValue;
                        }
                    }
                }
            }
            Store[] stores;
            Customer[] customers;
            Dictionary<Store, float> distFromCustomerToStore;

            stores = new Store[pP];
            for (int j = 0; j < pP; j++)
                stores[j] = new Store(j + 1, producerGive[j]);

            customers = new Customer[cP];

            for (int i = 0; i < cP; i++)
            {
                distFromCustomerToStore = new Dictionary<Store, float>();
                for (int j = 0; j < pP; j++)
                    distFromCustomerToStore.Add(stores[j], distances[i][j]);
                customers[i] = new Customer(i + 1, consumerRequests[i], distFromCustomerToStore);
            }

            // Добавляем магазины и клиентов в симуляцию
            sim.Stores.AddRange(stores);
            sim.Customers.AddRange(customers);

            sim.Run(); // Запускаем симуляцию для распределения энергии между потребителями и производителями
        }






        /// <summary>
        /// Тикаем
        /// </summary>
        /// <param name="deltaTime"></param>
        private void OnGameTick(float deltaTime)
        {

            //Очищаем старые пути
            if (api.World.Rand.NextDouble() < 0.1d)
            {
                PathCacheManager.Cleanup();
            }


            Cleaner();

            foreach (var network in networks)
            {
                // Этап 1: Очистка ----------------------------------------------------------------------------
                producers.Clear();
                consumers.Clear();
                accums.Clear();


                // Этап 2: Сбор запросов от потребителей----------------------------------------------------------------------------
                var cons = network.Consumers.Count;                              // Количество потребителей в сети
                var consIter = 0;                                                // Итератор для потребителей
                float requestedEnergy;                                           // Запрошенная энергия от потребителей
                consumerPositions = new BlockPos[cons];                          // Позиции потребителей
                consumerRequests = new float[cons];                              // Запросы потребителей

                foreach (var electricConsumer in network.Consumers)
                {
                    if (parts.TryGetValue(electricConsumer.Pos, out var part) && part.IsLoaded)
                    {
                        consumers.Add(new Consumer(electricConsumer));
                        requestedEnergy = electricConsumer.Consume_request();
                        consumerPositions[consIter] = electricConsumer.Pos;
                        consumerRequests[consIter] = requestedEnergy;
                        consIter++;
                    }
                }

                // Этап 3: Сбор энергии с генераторов и аккумуляторов----------------------------------------------------------------------------
                var prod = network.Producers.Count + network.Accumulators.Count; // Количество производителей в сети
                int prodIter = 0;                                                // Итератор для производителей
                float giveEnergy;                                                // Энергия, которую отдают производители
                producerPositions = new BlockPos[prod];                          // Позиции производителей
                producerGive = new float[prod];                                  // Энергия, которую отдают производители

                foreach (var electricProducer in network.Producers)
                {
                    if (parts.TryGetValue(electricProducer.Pos, out var part) && part.IsLoaded)
                    {
                        producers.Add(new Producer(electricProducer));
                        giveEnergy = electricProducer.Produce_give();
                        producerPositions[prodIter] = electricProducer.Pos;
                        producerGive[prodIter] = giveEnergy;
                        prodIter++;
                    }
                }

                foreach (var electricAccum in network.Accumulators)
                {
                    if (parts.TryGetValue(electricAccum.Pos, out var part) && part.IsLoaded)
                    {
                        accums.Add(new Accumulator(electricAccum));
                        giveEnergy =  electricAccum.canRelease();
                        producerPositions[prodIter] = electricAccum.Pos;
                        producerGive[prodIter] = giveEnergy;
                        prodIter++;
                    }
                }

                // Этап 4: Распределение энергии ----------------------------------------------------------------------------
                sim.Reset();                                                        // Сбрасываем состояние симуляции
                logisticalTask(network, ref consumerPositions, ref consumerRequests, ref producerPositions, ref producerGive,
                    ref sim);



                if (!instant) // Медленная передача
                {
                    BlockPos posStore; // Позиция магазина в мире
                    BlockPos posCustomer; // Позиция потребителя в мире

                    foreach (var customer in sim.Customers)
                    {
                        foreach (var store in sim.Stores)
                        {

                            if (customer.Received.TryGetValue(store, out var value))
                            {
                                posStore = producerPositions[sim.Stores.IndexOf(store)];
                                posCustomer = consumerPositions[sim.Customers.IndexOf(customer)];

                                if (PathCacheManager.TryGet(posCustomer, posStore, network.version, out var path, out var facing, out var processed, out var usedConn))
                                {
                                    // Проверяем, что пути и направления не равны null
                                    if (path == null ||
                                        facing == null ||
                                        processed == null ||
                                        usedConn == null)
                                        continue;

                                    // создаём пакет, не копируя ничего
                                    var packet = new EnergyPacket(
                                        value,
                                        parts[posStore].eparams[facing.Last()].voltage,
                                        path.Length - 1,
                                        path,
                                        facing,
                                        processed,
                                        usedConn
                                        );


                                    // Добавляем пакет в глобальный список
                                    globalEnergyPackets.Add(packet);
                                }

                            }


                        }
                    }
                }


                if (instant) // Мгновенная передача
                {
                    int i = 0;
                    float totalGive;                        // Суммарная энергия, которую нужно отдать
                    foreach (var consumer in consumers)
                    {
                        totalGive = sim.Customers[i].Required - sim.Customers[i].Remaining;
                        consumer.ElectricConsumer.Consume_receive(totalGive);
                        i++;
                    }
                }



                // Этап 5: Забираем у аккумуляторов выданное    ----------------------------------------------------------------------------
                consIter = 0;                                                     // Итератор
                foreach (var accum in accums)
                {
                    if (sim.Stores[consIter + producers.Count].Stock < accum.ElectricAccum.canRelease())
                    {
                        accum.ElectricAccum.Release(accum.ElectricAccum.canRelease() - sim.Stores[consIter + producers.Count].Stock);
                    }
                    consIter++;
                }


                // Этап 6: Зарядка аккумуляторов    ----------------------------------------------------------------------------
                cons = accums.Count;                                              // Количество аккумов в сети
                consIter = 0;                                                     // Итератор для потребителей
                consumer2Positions = new BlockPos[cons];                          // Позиции потребителей
                consumer2Requests = new float[cons];                              // Запросы потребителей

                foreach (var accum in accums)
                {
                    requestedEnergy = accum.ElectricAccum.canStore();
                    consumer2Positions[consIter] = accum.ElectricAccum.Pos;
                    consumer2Requests[consIter] = requestedEnergy;
                    consIter++;
                }



                // Этап 7: Остатки генераторов  ----------------------------------------------------------------------------
                prod = producers.Count;                                          // Количество производителей в сети
                prodIter = 0;                                                    // Итератор для производителей
                producer2Positions = new BlockPos[prod];                          // Позиции производителей
                producer2Give = new float[prod];                                  // Энергия, которую отдают производители

                foreach (var producer in producers)
                {
                    giveEnergy = sim.Stores[prodIter].Stock;
                    producer2Positions[prodIter] = producer.ElectricProducer.Pos;
                    producer2Give[prodIter] = giveEnergy;
                    prodIter++;
                }


                // Этап 8: Распределение энергии для аккумуляторов ----------------------------------------------------------------------------
                sim2.Reset();            // Сбрасываем состояние симуляции
                logisticalTask(network, ref consumer2Positions, ref consumer2Requests, ref producer2Positions, ref producer2Give, ref sim2);

                if (!instant) // Медленная передача
                {
                    BlockPos posStore; // Позиция магазина в мире
                    BlockPos posCustomer; // Позиция потребителя в мире

                    foreach (var customer in sim2.Customers)
                    {
                        foreach (var store in sim2.Stores)
                        {

                            if (customer.Received.TryGetValue(store, out var value))
                            {
                                posStore = producer2Positions[sim2.Stores.IndexOf(store)];
                                posCustomer = consumer2Positions[sim2.Customers.IndexOf(customer)];

                                if (PathCacheManager.TryGet(posCustomer, posStore, network.version, out var path, out var facing, out var processed, out var usedConn))
                                {
                                    // Проверяем, что пути и направления не равны null
                                    if (path == null ||
                                        facing == null ||
                                        processed == null ||
                                        usedConn == null)
                                        continue;

                                    // создаём пакет, не копируя ничего
                                    var packet = new EnergyPacket(
                                        value,
                                        parts[posStore].eparams[facing.Last()].voltage,
                                        path.Length - 1,
                                        path,
                                        facing,
                                        processed,
                                        usedConn
                                        );


                                    // Добавляем пакет в глобальный список
                                    globalEnergyPackets.Add(packet);
                                }

                            }
                        }
                    }
                }



                int j = 0;
                if (instant) // Мгновенная передача
                {
                    foreach (var accum in accums)
                    {
                        var totalGive = sim2.Customers[j].Required - sim2.Customers[j].Remaining;
                        accum.ElectricAccum.Store(totalGive);
                        j++;
                    }
                }

                // Этап 9: Сообщение генераторам о нагрузке ----------------------------------------------------------------------------
                j = 0;
                foreach (var producer in producers)
                {
                    var totalOrder = sim.Stores[j].totalRequest + sim2.Stores[j].totalRequest;
                    producer.ElectricProducer.Produce_order(totalOrder);
                    j++;
                }



                // Этап 10: Обновление статистики сети ----------------------------------------------------------------------------

                // Расчет потребления (только потребителями)
                float consumption = 0f;

                // потребление в первой симуляции
                foreach (var customer in sim.Customers)
                {
                    foreach (var store in sim.Stores)
                    {
                        if (customer.Received.TryGetValue(store, out var value))
                        {
                            consumption += value;
                        }
                    }
                }


                network.Consumption = consumption;


                // Расчет производства (чистая генерация генераторами)
                float production = 0f;
                foreach (var producer in producers)
                {
                    production += Math.Min(producer.ElectricProducer.getPowerGive(), producer.ElectricProducer.getPowerOrder());
                }
                network.Production = production;


                // Расчет необходимой энергии для потребителей!
                float requestSum = 0f;
                foreach (var consumer in consumers)
                {
                    requestSum += consumer.ElectricConsumer.getPowerRequest();
                }

                network.Request = Math.Max(requestSum, 0f);



                float capacity = 0f; // Суммарная емкость сети
                float maxCapacity = 0f; // Максимальная емкость сети

                foreach (var a in accums)
                {
                    capacity += a.ElectricAccum.GetCapacity();
                    maxCapacity += a.ElectricAccum.GetMaxCapacity();
                }

                network.Capacity = capacity;
                network.MaxCapacity = maxCapacity;




            }


            // Обновление электрических компонентов в сети, если прошло достаточно времени около 0.5 секунд
            elapsedMs += deltaTime;
            if (elapsedMs > 0.5f)
            {
                foreach (var network in networks)
                {
                    foreach (var electricTransformator in network.Transformators)
                    {
                        if (parts.TryGetValue(electricTransformator.Pos, out var part) && part.IsLoaded)
                            electricTransformator.Update();
                    }

                    foreach (var electricConductor in network.Conductors)
                    {
                        if (parts.TryGetValue(electricConductor.Pos, out var part) && part.IsLoaded)
                            electricConductor.Update();
                    }

                    foreach (var electricConsumer in network.Consumers)
                    {
                        if (parts.TryGetValue(electricConsumer.Pos, out var part) && part.IsLoaded)
                            electricConsumer.Update();
                    }

                    foreach (var electricProducer in network.Producers)
                    {
                        if (parts.TryGetValue(electricProducer.Pos, out var part) && part.IsLoaded)
                            electricProducer.Update();
                    }

                    foreach (var electricAccum in network.Accumulators)
                    {
                        if (parts.TryGetValue(electricAccum.Pos, out var part) && part.IsLoaded)
                            electricAccum.Update();
                    }
                }

                elapsedMs = 0f; // сбросить накопленное время
            }






            if (!instant) // Если не мгновенная передача, то продолжаем обработку пакетов
            {
                // Этап 11: Потребление энергии пакетами и Этап 12: Перемещение пакетов-----------------------------------------------

                BlockPos pos;                   // Временная переменная для позиции
                float resistance, current, lossEnergy;  // Переменные для расчета сопротивления, тока и потерь энергии                    
                int curIndex, currentFacingFrom;        // текущий индекс и направление в пакете
                BlockPos currentPos, nextPos;           // текущая и следующая позиции в пути пакета
                NetworkPart nextPart, currentPart;      // Временные переменные для частей сети
                EnergyPacket packet;                    // Временная переменная для пакета энергии

                sumEnergy.Clear();
                foreach (var part2 in parts)  //перебираем все элементы
                {
                    sumEnergy.Add(part2.Key, 0F);         //заполняем нулями               

                    part2.Value.current.Fill(0f);           //обнуляем токи
                }



                for (int i = globalEnergyPackets.Count - 1; i >= 0; i--)
                {
                    packet = globalEnergyPackets[i];
                    curIndex = packet.currentIndex; //текущий индекс в пакете

                    if (curIndex == 0)
                    {
                        pos = packet.path[0];
                        if (parts.TryGetValue(pos, out var part2))
                        {
                            bool isValid = false;
                            // Ручная проверка условий 
                            foreach (var s in part2.eparams)
                            {
                                if (s.voltage > 0 && !s.burnout && packet.voltage >= s.voltage)
                                {
                                    isValid = true;
                                    break;
                                }
                            }

                            if (isValid)
                            {
                                if (sumEnergy.TryGetValue(pos, out _))
                                {
                                    sumEnergy[pos] += packet.energy;
                                }
                                else
                                {
                                    sumEnergy.Add(pos, packet.energy);
                                }
                            }
                        }

                        globalEnergyPackets.RemoveAt(i); //удаляем пакеты, которые не могут быть переданы дальше
                    }
                    else
                    {

                        currentPos = packet.path[curIndex];
                        nextPos = packet.path[curIndex - 1];
                        currentFacingFrom = packet.facingFrom[curIndex];

                        if (parts.TryGetValue(nextPos, out nextPart!) &&
                            parts.TryGetValue(currentPos, out currentPart!))
                        {
                            if (!nextPart.eparams[packet.facingFrom[curIndex - 1]].burnout) //проверяем не сгорела ли грань в след блоке
                            {

                                if ((nextPart.Connection & packet.usedConnections[curIndex - 1]) == packet.usedConnections[curIndex - 1]) // проверяем совпадает ли путь в пакете с путем в части сети

                                {
                                    // считаем сопротивление
                                    resistance = currentPart.eparams[currentFacingFrom].resistivity /
                                                   (currentPart.eparams[currentFacingFrom].lines *
                                                    currentPart.eparams[currentFacingFrom].crossArea);

                                    // Провод в изоляции теряет меньше энергии
                                    if (currentPart.eparams[currentFacingFrom].isolated)
                                        resistance /= 2.0f;

                                    // считаем ток по закону Ома
                                    current = packet.energy / packet.voltage;

                                    // считаем потерю энергии по закону Джоуля
                                    lossEnergy = current * current * resistance;
                                    packet.energy = Math.Max(packet.energy - lossEnergy, 0);

                                    // пересчитаем ток уже с учетом потерь
                                    current = packet.energy / packet.voltage;


                                    packet.currentIndex--;

                                    // далее учитываем правило алгебраического сложения встречных токов
                                    // 1) Определяем вектор движени
                                    var delta = nextPos.SubCopy(currentPos);
                                    bool sign = true;
                                    
                                    if (delta.X < 0)    sign = !sign;
                                   
                                    if (delta.Y < 0)    sign = !sign;

                                    if (delta.Z < 0)    sign = !sign;

                                    // 2) Прописываем токи на нужные грани
                                    int j = 0;
                                    foreach (var face in packet.nowProcessedFaces[packet.currentIndex])
                                    {
                                        if (face)
                                        {
                                            if (sign)
                                                nextPart.current[j] += current; // добавляем ток в следующую часть сети
                                            else
                                                nextPart.current[j] -= current; // добавляем ток в следующую часть сети
                                        }
                                        j++;
                                    }

                                    // 3) Если энергия пакета почти нулевая — удаляем пакет
                                    if (packet.energy <= 0.001f)
                                    {
                                        globalEnergyPackets.RemoveAt(i);
                                    }


                                }
                                else
                                {
                                    // если все же путь не совпадает с путем в пакете, то чистим кэши
                                    PathCacheManager.RemoveAll(packet.path[0], packet.path.Last());

                                    globalEnergyPackets.RemoveAt(i);

                                }
                            }
                            else
                            {
                                globalEnergyPackets.RemoveAt(i);
                            }
                        }
                        else
                        {
                            // если все же части сети не найдены, то тут точно кэш надо утилизировать
                            PathCacheManager.RemoveAll(packet.path[0], packet.path.Last());

                            globalEnergyPackets.RemoveAt(i);

                        }

                    }
                }

                

                foreach (var pair in sumEnergy)
                {
                    if (parts[pair.Key].Consumer != null)
                        parts[pair.Key].Consumer!.Consume_receive(pair.Value);
                    else if (parts[pair.Key].Accumulator != null)
                        parts[pair.Key].Accumulator!.Store(pair.Value);
                }




                // Этап 13: Проверка сгорания проводов и трансформаторов ----------------------------------------------------------------------------


                packetsToRemove.Clear(); // Список пакетов для удаления после проверки на сгорание
                                         // раньше его использовать нет смысла

                packetsByPosition.Clear();
                foreach (var packet2 in globalEnergyPackets)
                {
                    pos = packet2.path[packet2.currentIndex];
                    if (!packetsByPosition.TryGetValue(pos, out var list))
                    {
                        list = new List<EnergyPacket>();
                        packetsByPosition[pos] = list;
                    }
                    list.Add(packet2);
                }



                var bAccessor = api.World.BlockAccessor; //аксессор для блоков
                BlockPos partPos;                        // Временная переменная для позиции части сети
                NetworkPart part;                        // Временная переменная для части сети
                bool updated;                            // Флаг обновления части сети от повреждения
                EParams faceParams;                      // Параметры грани сети
                int lastFaceIndex;                       // Индекс последней грани в пакете
                float totalEnergy;                       // Суммарная энергия в трансформаторе
                float totalCurrent;                      // Суммарный ток в трансформаторе
                int k = 0;

                foreach (var partEntry in parts)
                {
                    partPos = partEntry.Key;
                    part = partEntry.Value;

                    //обновляем каждый блок сети
                    updated = k % 20 == envUpdater && 
                              part.IsLoaded &&          // блок загружен?
                              damageManager!.DamageByEnvironment(this.sapi, ref part, ref bAccessor);
                    k++;


                    if (updated)
                    {

                        for (int faceIndex = 0; faceIndex < 6; faceIndex++)
                        {
                            faceParams = part.eparams[faceIndex];
                            if (faceParams.voltage == 0 || !faceParams.burnout)
                                continue;

                            
                            ResetComponents(ref part);
                        }

                    }


                    // Обработка трансформаторов
                    if (part.Transformator != null && packetsByPosition.TryGetValue(partPos, out var packets))
                    {
                        totalEnergy = 0f;
                        totalCurrent = 0f;

                        foreach (var packet2 in packets)
                        {

                            totalEnergy += packet2.energy;
                            totalCurrent += packet2.energy / packet2.voltage;


                            if (packet2.voltage == part.Transformator.highVoltage)
                                packet2.voltage = part.Transformator.lowVoltage;
                            else if (packet2.voltage == part.Transformator.lowVoltage)
                                packet2.voltage = part.Transformator.highVoltage;

                        }


                        int transformatorFaceIndex = FacingHelper.GetFaceIndex(FacingHelper.FromFace(FacingHelper.Faces(part.Connection).First())); // Индекс грани трансформатора!

                        part.current[transformatorFaceIndex] = totalCurrent;

                        part.Transformator.setPower(totalEnergy);
                        part.Transformator.Update();
                    }

                    // Проверка на превышение напряжения
                    if (packetsByPosition.TryGetValue(partPos, out var positionPackets))
                    {
                        foreach (var packet2 in positionPackets)
                        {

                            lastFaceIndex = packet2.facingFrom[packet2.currentIndex];

                            faceParams = part.eparams[lastFaceIndex];
                            if (faceParams.voltage != 0 && packet2.voltage > faceParams.voltage)
                            {
                                part.eparams[lastFaceIndex].prepareForBurnout(2);

                                if (packet2.path[packet2.currentIndex] == partPos)
                                    packetsToRemove.Add(packet2);


                                ResetComponents(ref part);
                                break;
                            }
                        }
                    }

                    // Проверка на превышение тока
                    for (int faceIndex = 0; faceIndex < 6; faceIndex++)
                    {

                        faceParams = part.eparams[faceIndex];
                        if (faceParams.voltage == 0 ||
                            part.current[faceIndex] <= faceParams.maxCurrent * faceParams.lines)
                            continue;

                        part.eparams[faceIndex].prepareForBurnout(1);

                        packetsToRemove.AddRange(
                            globalEnergyPackets.Where(p =>
                                p.path[p.currentIndex] == partPos &&
                                p.nowProcessedFaces.LastOrDefault()?[faceIndex] == true
                            )
                        );

                        ResetComponents(ref part);
                    }
                }


                envUpdater++;
                if (envUpdater > 19)
                    envUpdater = 0;

                // Пакетное удаление
                globalEnergyPackets.RemoveAll(p => packetsToRemove.Contains(p));
                packetsToRemove.Clear();




            }


        }


        // Вынесенный метод сброса компонентов
        private void ResetComponents(ref NetworkPart part)
        {
            part.Consumer?.Consume_receive(0f);
            part.Producer?.Produce_order(0f);
            part.Accumulator?.SetCapacity(0f);
            part.Transformator?.setPower(0f);
        }




        /// <summary>
        /// Объединение цепей
        /// </summary>
        /// <param name="networks"></param>
        /// <returns></returns>
        private Network MergeNetworks(HashSet<Network> networks)
        {
            Network? outNetwork = null;

            foreach (var network in networks)
            {
                if (outNetwork == null || outNetwork.PartPositions.Count < network.PartPositions.Count)
                {
                    outNetwork = network;
                }
            }

            if (outNetwork != null)
            {
                foreach (var network in networks)
                {
                    if (outNetwork == network)
                    {
                        continue;
                    }

                    foreach (var position in network.PartPositions)
                    {
                        var part = this.parts[position];
                        foreach (var face in BlockFacing.ALLFACES)
                        {
                            if (part.Networks[face.Index] == network)
                            {
                                part.Networks[face.Index] = outNetwork;
                            }
                        }

                        if (part.Conductor is { } conductor) outNetwork.Conductors.Add(conductor);
                        if (part.Consumer is { } consumer) outNetwork.Consumers.Add(consumer);
                        if (part.Producer is { } producer) outNetwork.Producers.Add(producer);
                        if (part.Accumulator is { } accumulator) outNetwork.Accumulators.Add(accumulator);
                        if (part.Transformator is { } transformator) outNetwork.Transformators.Add(transformator);

                        outNetwork.PartPositions.Add(position);
                    }

                    network.PartPositions.Clear();
                    this.networks.Remove(network);
                }
            }

            outNetwork ??= this.CreateNetwork();

            return outNetwork;
        }



        /// <summary>
        /// Удаляем сеть
        /// </summary>
        /// <param name="network"></param>
        private void RemoveNetwork(ref Network network)
        {
            var partPositions = new BlockPos[network.PartPositions.Count];
            network.PartPositions.CopyTo(partPositions);
            network.version++;                                              // Увеличиваем версию сети перед удалением
            this.networks.Remove(network);                                  //удаляем цепь из списка цепей

            foreach (var position in partPositions)                         //перебираем по всем бывшим элементам этой цепи
            {
                if (this.parts.TryGetValue(position, out var part))         //есть такое соединение?
                {
                    foreach (var face in BlockFacing.ALLFACES)              //перебираем по всем 6 направлениям
                    {
                        if (part.Networks[face.Index] == network)           //если нашли привязку к этой цепи
                        {
                            part.Networks[face.Index] = null;               //обнуляем ее
                        }
                    }
                }
            }

            foreach (var position in partPositions)                                 //перебираем по всем бывшим элементам этой цепи
            {
                if (this.parts.TryGetValue(position, out var part))                 //есть такое соединение?
                {
                    this.AddConnections(ref part, part.Connection, (new EParams(), 0));     //добавляем соединения???
                }
            }
        }


        /// <summary>
        /// Создаем новую цепь
        /// </summary>
        /// <returns></returns>
        private Network CreateNetwork()
        {
            var network = new Network();
            this.networks.Add(network);

            return network;
        }


        /// <summary>
        /// Добавляем соединения
        /// </summary>
        /// <param name="part"></param>
        /// <param name="addedConnections"></param>
        /// <param name="setEparams"></param>
        /// <exception cref="Exception"></exception>
        private void AddConnections(ref NetworkPart part, Facing addedConnections, (EParams, int) setEparams)
        {
            var networksByFace = new[]
            {
            new HashSet<Network>(),
            new HashSet<Network>(),
            new HashSet<Network>(),
            new HashSet<Network>(),
            new HashSet<Network>(),
            new HashSet<Network>()
            };

            foreach (var face in FacingHelper.Faces(part.Connection))           //ищет к каким сетям эти провода могут относиться
            {
                networksByFace[face.Index].Add(part.Networks[face.Index] ?? this.CreateNetwork());
            }


            //поиск соседей по граням
            foreach (var direction in FacingHelper.Directions(addedConnections))
            {
                var directionFilter = FacingHelper.FromDirection(direction);
                var neighborPosition = part.Position.AddCopy(direction);

                if (this.parts.TryGetValue(neighborPosition, out var neighborPart))         //проверяет, если в той стороне сосед
                {
                    foreach (var face in FacingHelper.Faces(addedConnections & directionFilter))
                    {
                        // 1) Соединение своей грани face с противоположной гранью соседа
                        if ((neighborPart.Connection & FacingHelper.From(face, direction.Opposite)) != 0)
                        {
                            if (neighborPart.Networks[face.Index] is { } network)
                            {
                                networksByFace[face.Index].Add(network);
                            }
                        }

                        // 2) Тоже, но наоборот
                        if ((neighborPart.Connection & FacingHelper.From(direction.Opposite, face)) != 0)
                        {
                            if (neighborPart.Networks[direction.Opposite.Index] is { } network)
                            {
                                networksByFace[face.Index].Add(network);
                            }
                        }
                    }
                }

                //поиск соседей по ребрам
                directionFilter = FacingHelper.FromDirection(direction);

                foreach (var face in FacingHelper.Faces(addedConnections & directionFilter))
                {
                    neighborPosition = part.Position.AddCopy(direction).AddCopy(face);

                    if (this.parts.TryGetValue(neighborPosition, out neighborPart))
                    {
                        // 1) Проверяем соединение через ребро direction–face
                        if ((neighborPart.Connection & FacingHelper.From(direction.Opposite, face.Opposite)) != 0)
                        {
                            if (neighborPart.Networks[direction.Opposite.Index] is { } network)
                            {
                                networksByFace[face.Index].Add(network);
                            }
                        }

                        // 2) Тоже, но наоборот
                        if ((neighborPart.Connection & FacingHelper.From(face.Opposite, direction.Opposite)) != 0)
                        {
                            if (neighborPart.Networks[face.Opposite.Index] is { } network)
                            {
                                networksByFace[face.Index].Add(network);
                            }
                        }
                    }
                }


                // ищем соседей по перпендикулярной грани
                directionFilter = FacingHelper.FromDirection(direction);

                foreach (var face in FacingHelper.Faces(addedConnections & directionFilter))
                {
                    neighborPosition = part.Position.AddCopy(face);

                    if (this.parts.TryGetValue(neighborPosition, out neighborPart))
                    {
                        // 1) Проверяем перпендикулярную грань соседа
                        if ((neighborPart.Connection & FacingHelper.From(direction, face.Opposite)) != 0)
                        {
                            if (neighborPart.Networks[direction.Index] is { } network)
                            {
                                networksByFace[face.Index].Add(network);
                            }
                        }

                        // 2) Тоже, но наоборот
                        if ((neighborPart.Connection & FacingHelper.From(face.Opposite, direction)) != 0)
                        {
                            if (neighborPart.Networks[face.Opposite.Index] is { } network)
                            {
                                networksByFace[face.Index].Add(network);
                            }
                        }

                    }
                }
            }








            foreach (var face in FacingHelper.Faces(part.Connection))
            {
                var network = this.MergeNetworks(networksByFace[face.Index]);

                if (part.Conductor is { } conductor)
                {
                    network.Conductors.Add(conductor);
                }

                if (part.Consumer is { } consumer)
                {
                    network.Consumers.Add(consumer);
                }

                if (part.Producer is { } producer)
                {
                    network.Producers.Add(producer);
                }

                if (part.Accumulator is { } accumulator)
                {
                    network.Accumulators.Add(accumulator);
                }

                if (part.Transformator is { } transformator)
                {
                    network.Transformators.Add(transformator);
                }



                network.PartPositions.Add(part.Position);

                network.version++; // Увеличиваем версию сети после добавления соединения

                part.Networks[face.Index] = network;            //присваиваем в этой точке эту цепь

                int i = 0;
                if (part.eparams == null)
                {
                    part.eparams = new EParams[]
                            {
                        new EParams(),
                        new EParams(),
                        new EParams(),
                        new EParams(),
                        new EParams(),
                        new EParams()
                            };
                }

                foreach (var ams in part.eparams)
                {
                    if (ams.Equals(new EParams()))
                        part.eparams[i] = new EParams();
                    i++;
                }

                if (!setEparams.Item1.Equals(new EParams()) && part.eparams[face.Index].maxCurrent == 0)
                    part.eparams[face.Index] = setEparams.Item1;      //аналогично с параметрами электричества
            }





            foreach (var direction in FacingHelper.Directions(part.Connection))
            {
                var directionFilter = FacingHelper.FromDirection(direction);

                foreach (var face in FacingHelper.Faces(part.Connection & directionFilter))
                {
                    if ((part.Connection & FacingHelper.From(direction, face)) != 0)
                    {
                        if (part.Networks[face.Index] is { } network1 && part.Networks[direction.Index] is { } network2)
                        {
                            var networks = new HashSet<Network>
                        {
                            network1, network2
                        };

                            this.MergeNetworks(networks);
                        }
                        else
                        {
                            throw new Exception();
                        }
                    }
                }
            }


        }



        /// <summary>
        /// Удаляем соединения
        /// </summary>
        /// <param name="part"></param>
        /// <param name="removedConnections"></param>
        private void RemoveConnections(ref NetworkPart part, Facing removedConnections)
        {
            foreach (var blockFacing in FacingHelper.Faces(removedConnections))
            {
                if (part.Networks[blockFacing.Index] is { } network)
                {
                    this.RemoveNetwork(ref network);
                    network.version++; // Увеличиваем версию сети после удаления
                }
            }
        }



        /// <summary>
        /// Задать проводник
        /// </summary>
        /// <param name="position"></param>
        /// <param name="conductor"></param>
        public void SetConductor(BlockPos position, IElectricConductor? conductor) =>
        SetComponent(
            position,
            conductor,
            part => part.Conductor,
            (part, c) => part.Conductor = c,
            network => network.Conductors);




        /// <summary>
        /// Задать потребителя
        /// </summary>
        /// <param name="position"></param>
        /// <param name="consumer"></param>
        public void SetConsumer(BlockPos position, IElectricConsumer? consumer) =>
        SetComponent(
            position,
            consumer,
            part => part.Consumer,
            (part, c) => part.Consumer = c,
            network => network.Consumers);


        /// <summary>
        /// Задать генератор
        /// </summary>
        /// <param name="position"></param>
        /// <param name="producer"></param>
        public void SetProducer(BlockPos position, IElectricProducer? producer) =>
            SetComponent(
                position,
                producer,
                part => part.Producer,
                (part, p) => part.Producer = p,
                network => network.Producers);


        /// <summary>
        /// Задать аккумулятор
        /// </summary>
        /// <param name="position"></param>
        /// <param name="accumulator"></param>
        public void SetAccumulator(BlockPos position, IElectricAccumulator? accumulator) =>
            SetComponent(
                position,
                accumulator,
                part => part.Accumulator,
                (part, a) => part.Accumulator = a,
                network => network.Accumulators);


        /// <summary>
        /// Задать трансформатор
        /// </summary>
        /// <param name="position"></param>
        /// <param name="transformator"></param>
        public void SetTransformator(BlockPos position, IElectricTransformator? transformator) =>
            SetComponent(
                position,
                transformator,
                part => part.Transformator,
                (part, a) => part.Transformator = a,
                network => network.Transformators);


        /// <summary>
        /// Задает компоненты разных типов
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="position"></param>
        /// <param name="newComponent"></param>
        /// <param name="getComponent"></param>
        /// <param name="setComponent"></param>
        /// <param name="getCollection"></param>
        private void SetComponent<T>(
            BlockPos position,
            T? newComponent,
            System.Func<NetworkPart, T?> getComponent,
            Action<NetworkPart, T?> setComponent,
            System.Func<Network, ICollection<T>> getCollection)
            where T : class
        {
            if (!this.parts.TryGetValue(position, out var part))
            {
                if (newComponent == null)
                {
                    return;
                }

                part = this.parts[position] = new NetworkPart(position);
            }

            var oldComponent = getComponent(part);
            if (oldComponent != newComponent)
            {
                foreach (var network in part.Networks)
                {
                    if (network is null) continue;

                    var collection = getCollection(network);

                    if (oldComponent != null)
                    {
                        collection.Remove(oldComponent);
                    }

                    if (newComponent != null)
                    {
                        collection.Add(newComponent);
                    }
                }

                setComponent(part, newComponent);
            }
        }





        /// <summary>
        /// Cобирает информацию по цепи
        /// </summary>
        /// <param name="position"></param>
        /// <param name="facing"></param>
        /// <param name="method">Метод вывода с какой грани "thisFace"- эту грань, "firstFace"- информация о первой грани из многих, "currentFace" - информация о грани, в которой ток больше 0</param>
        /// <returns></returns>
        public NetworkInformation GetNetworks(BlockPos position, Facing facing, string method = "thisFace")
        {
            var result = new NetworkInformation(); // результат вываливается сюда

            if (this.parts.TryGetValue(position, out var part))
            {
                Network network = null!;

                if (method == "thisFace" || method == "firstFace") // пока так, возможно потом по-разному будет обработка
                {
                    var blockFacing = FacingHelper.Faces(facing).First();

                    if (part.Networks[blockFacing.Index] is { } net)
                    {
                        network = net;                                              //выдаем найденную цепь
                        result.Facing |= FacingHelper.FromFace(blockFacing);        //выдаем ее направления
                        result.eParamsInNetwork = part.eparams[blockFacing.Index];  //выдаем ее текущие параметры
                        result.current = part.current[blockFacing.Index];           //выдаем текущий ток в этой грани
                    }
                    else
                        return result;
                }
                else if (method == "currentFace") // если ток больше нуля, то выдаем информацию о грани, в которой ток больше нуля
                {
                    var searchIndex = 0;
                    BlockFacing blockFacing = null!;

                    foreach (BlockFacing blockFacing2 in FacingHelper.Faces(facing))
                    {
                        if (part.Networks[blockFacing2.Index] is { } networkk && part.current[blockFacing2.Index] > 0.0F)
                        {
                            blockFacing = blockFacing2;
                            searchIndex = blockFacing2.Index;
                        }
                    }

                    if (part.Networks[searchIndex] is { } net)
                    {
                        network = net;                                              //выдаем найденную цепь
                        result.Facing |= FacingHelper.FromFace(blockFacing);        //выдаем ее направления
                        result.eParamsInNetwork = part.eparams[searchIndex];  //выдаем ее текущие параметры
                        result.current = part.current[searchIndex];           //выдаем текущий ток в этой грани
                    }
                    else
                        return result;
                }




                // Если нашли сеть, то заполняем информацию о ней
                result.NumberOfBlocks = network.PartPositions.Count;
                result.NumberOfConsumers = network.Consumers.Count;
                result.NumberOfProducers = network.Producers.Count;
                result.NumberOfAccumulators = network.Accumulators.Count;
                result.NumberOfTransformators = network.Transformators.Count;
                result.Production = network.Production;
                result.Consumption = network.Consumption;
                result.Capacity = network.Capacity;
                result.MaxCapacity = network.MaxCapacity;
                result.Request = network.Request;

            }

            return result;
        }


    }


    /// <summary>
    /// Проводник тока
    /// </summary>
    internal class Conductor
    {
        public readonly IElectricConductor ElectricConductor;
        public Conductor(IElectricConductor electricConductor) => ElectricConductor = electricConductor;
    }


    /// <summary>
    /// Потребитель
    /// </summary>
    internal class Consumer
    {
        public readonly IElectricConsumer ElectricConsumer;
        public Consumer(IElectricConsumer electricConsumer) => ElectricConsumer = electricConsumer;
    }


    /// <summary>
    /// Трансформатор
    /// </summary>
    internal class Transformator
    {
        public readonly IElectricTransformator ElectricTransformator;
        public Transformator(IElectricTransformator electricTransformator) => ElectricTransformator = electricTransformator;
    }


    /// <summary>
    /// Генератор
    /// </summary>
    internal class Producer
    {
        public readonly IElectricProducer ElectricProducer;
        public Producer(IElectricProducer electricProducer) => ElectricProducer = electricProducer;
    }


    /// <summary>
    /// Аккумулятор
    /// </summary>
    internal class Accumulator
    {
        public readonly IElectricAccumulator ElectricAccum;
        public Accumulator(IElectricAccumulator electricAccum) => ElectricAccum = electricAccum;
    }




    /// <summary>
    /// Конфигуратор сети
    /// </summary>
    public class ElectricityConfig
    {
        public int speedOfElectricity = 4;
        public bool instant = false;
        public int timeBeforeBurnout = 30;
    }

    /// <summary>
    /// Кэш путей
    /// </summary>
    public struct PathCacheEntry
    {
        public BlockPos[]? Path;
        public int[]? FacingFrom;
        public bool[][]? NowProcessedFaces;
        public Facing[]? usedConnections;
        public int Version;
    }
}