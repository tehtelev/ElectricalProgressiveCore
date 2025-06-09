using Vintagestory.API.Common;
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
using System.Threading.Channels;
using static ElectricalProgressive.ElectricalProgressive;
using ProtoBuf;
using Vintagestory.API.Util;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Diagnostics;

[assembly: ModDependency("game", "1.20.0")]
[assembly: ModInfo(
    "Electrical Progressive: Core",
    "electricalprogressivecore",
    Website = "https://github.com/tehtelev/ElectricalProgressiveCore",
    Description = "Brings electricity into the game!",
    Version = "1.0.4",
    Authors = new[] { "Tehtelev", "Kotl" }
)]

namespace ElectricalProgressive
{
    public class ElectricalProgressive : ModSystem
    {
        private readonly List<Consumer> consumers = new();
        private readonly List<Producer> producers = new();
        private readonly List<Accumulator> accums = new();
        private readonly List<Transformator> transformators = new();

        private readonly List<EnergyPacket> globalEnergyPackets = new(); // Глобальный список пакетов энергии

        private BlockPos[]? consumerPositions;
        private float[]? consumerRequests;
        private BlockPos[]? producerPositions;
        private float[]? producerGive;

        private BlockPos[]? consumer2Positions;
        private float[]? consumer2Requests;
        private BlockPos[]? producer2Positions;
        private float[]? producer2Give;



        Dictionary<BlockPos, float> sumEnergy = new Dictionary<BlockPos, float>();

        Dictionary<BlockPos, List<EnergyPacket>> packetsByPosition = new Dictionary<BlockPos, List<EnergyPacket>>(); //Словарь для хранения пакетов по позициям
        List<EnergyPacket> packetsToRemove = new List<EnergyPacket>(); // Список пакетов для удаления после проверки на сгорание


        public readonly HashSet<Network> networks = new();
        private readonly Dictionary<BlockPos, NetworkPart> parts = new(); // Хранит все элементы всех цепей

        public int speedOfElectricity; // Скорость электричества в проводах (блоков в тик)
        public bool instant; // Расчет мгновенно?
        private PathFinder pathFinder = new PathFinder(); // Модуль поиска путей
        public ICoreAPI api = null!;
        private ICoreClientAPI capi = null!;
        private ICoreServerAPI sapi = null!;
        private ElectricityConfig? config;
        public static DamageManager? damageManager;
        public static WeatherSystemServer? WeatherSystemServer;

        private Simulation sim = new Simulation();
        private Simulation sim2 = new Simulation();
        int tickTimeMs;

        int envUpdater = 0;

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

            api.Event.RegisterGameTickListener(this.OnGameTick, tickTimeMs);


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
        public bool Update(BlockPos position, Facing facing, (EParams, int) setEparams, ref EParams[] Eparams)
        {
            if (!parts.TryGetValue(position, out var part))
            {
                if (facing == Facing.None)
                    return false;
                part = parts[position] = new NetworkPart(position);
            }

            var addedConnections = ~part.Connection & facing;
            var removedConnections = part.Connection & ~facing;

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
        /// <param name="all"></param>
        public void Cleaner()
        {
            foreach (var part in parts.Values)
            {
                //не трогать тут ничего
                if (part.eparams != null && part.eparams.Length > 0)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        if (part.eparams[i].Equals(new EParams()))
                            part.eparams[i] = new EParams();
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
        /// <param name="paths"></param>
        /// <param name="facingFrom"></param>
        /// <param name="nowProcessedFaces"></param>
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
                            out var cachedFacing,
                            out var cachedProcessed,
                            out var cachedConnections)
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
        /// <param name="_"></param>
        private void OnGameTick(float _)
        {
            //int ticks = instant ? 1 : speedOfElectricity;


            //clean old path cache entries
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
                transformators.Clear();



                // Этап 2: Сбор запросов от потребителей----------------------------------------------------------------------------
                var cons = network.Consumers.Count;                              // Количество потребителей в сети
                var consIter = 0;                                                // Итератор для потребителей
                float requestedEnergy;                                           // Запрошенная энергия от потребителей
                consumerPositions = new BlockPos[cons];                          // Позиции потребителей
                consumerRequests = new float[cons];                              // Запросы потребителей

                foreach (var electricConsumer in network.Consumers)
                {
                    consumers.Add(new Consumer(electricConsumer));
                    requestedEnergy = electricConsumer.Consume_request();
                    consumerPositions[consIter] = electricConsumer.Pos;
                    consumerRequests[consIter] = requestedEnergy;
                    consIter++;
                }

                // Этап 3: Сбор энергии с генераторов и аккумуляторов----------------------------------------------------------------------------
                var prod = network.Producers.Count + network.Accumulators.Count; // Количество производителей в сети
                int prodIter = 0;                                                // Итератор для производителей
                float giveEnergy;                                                // Энергия, которую отдают производители
                producerPositions = new BlockPos[prod];                          // Позиции производителей
                producerGive = new float[prod];                                  // Энергия, которую отдают производители

                foreach (var electricProducer in network.Producers)
                {
                    producers.Add(new Producer(electricProducer));
                    giveEnergy = electricProducer.Produce_give();
                    producerPositions[prodIter] = electricProducer.Pos;
                    producerGive[prodIter] = giveEnergy;
                    prodIter++;
                }

                foreach (var electricAccum in network.Accumulators)
                {
                    accums.Add(new Accumulator(electricAccum));
                    giveEnergy = electricAccum.canRelease();
                    producerPositions[prodIter] = electricAccum.Pos;
                    producerGive[prodIter] = giveEnergy;
                    prodIter++;
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

                // Расчет потребления
                float consumption = 0f;
                foreach (var consumer in consumers)
                {
                    consumption += consumer.ElectricConsumer.getPowerReceive();
                }

                foreach (var accum in accums)
                {
                    consumption += Math.Max(accum.ElectricAccum.GetCapacity() - accum.ElectricAccum.GetLastCapacity(), 0f);
                }

                network.Consumption = consumption;

                // Расчет производства
                float production = 0f;
                foreach (var producer in producers)
                {
                    production += Math.Min(producer.ElectricProducer.getPowerGive(), producer.ElectricProducer.getPowerOrder());
                }
                network.Production = production;

                // Расчет необходимой энергии для сети
                float requestSum = 0f;
                foreach (var consumer in consumers)
                {
                    requestSum += consumer.ElectricConsumer.getPowerRequest();
                }

                network.Request = Math.Max(requestSum, 0f);

                // Обновление компонентов
                foreach (var electricTransformator in network.Transformators)
                {
                    transformators.Add(new Transformator(electricTransformator));
                }

                foreach (var a in accums)
                {
                    a.ElectricAccum.Update();
                }

                foreach (var p in producers)
                {
                    p.ElectricProducer.Update();
                }

                foreach (var c in consumers)
                {
                    c.ElectricConsumer.Update();
                }

                foreach (var t in transformators)
                {
                    t.ElectricTransformator.Update();
                }

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
                            // Ручная проверка условий вместо LINQ
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
                                if (sumEnergy.TryGetValue(pos, out var value))
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
                            parts.TryGetValue(currentPos, out currentPart!) &&
                            nextPart.Connection.HasFlag(packet.usedConnections[curIndex - 1]) &&
                            !nextPart.eparams[packet.facingFrom[curIndex - 1]].burnout)
                        {


                            // считаем сопротивление
                            resistance = currentPart.eparams[currentFacingFrom].resisitivity /
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

                            // пакет не бесполезен
                            if (packet.energy > 0.001f)
                            {

                                packet.currentIndex--; // уменьшаем индекс пакета

                                
                                int j = 0;
                                foreach (var face in packet.nowProcessedFaces.Last())
                                {
                                    if (face)
                                        nextPart.current[j] += current;
                                    j++;
                                }
                            }
                            else
                            {
                                globalEnergyPackets.RemoveAt(i);
                            }
                        }
                        else
                        {
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

                    //обновляем каждый 
                    updated = k % 20 == envUpdater &&
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


                        int transformatorFaceIndex = 5; // Индекс грани трансформатора!!!

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
                                part.eparams[lastFaceIndex].burnout = true;

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

                        part.eparams[faceIndex].burnout = true;


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

            part.Consumer?.Update();
            part.Producer?.Update();
            part.Accumulator?.Update();
            part.Transformator?.Update();

            //part.Consumer = null;
            //part.Producer = null;
            //part.Accumulator = null;
            //part.Transformator = null;
        }




        /// <summary>
        /// Обьединение цепей
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
        /// Cоздаем новую цепь
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
        /// <param name="accumulator"></param>
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
        public NetworkInformation GetNetworks(BlockPos position, Facing facing)
        {
            var result = new NetworkInformation();

            if (this.parts.TryGetValue(position, out var part))
            {
                var networks = new HashSet<Network>();

                foreach (var blockFacing in FacingHelper.Faces(facing))
                {
                    if (part.Networks[blockFacing.Index] is { } networkk)
                    {
                        networks.Add(networkk);                                     //выдаем найденную цепь
                        result.Facing |= FacingHelper.FromFace(blockFacing);        //выдаем ее направления
                        result.eParamsInNetwork = part.eparams[blockFacing.Index];                     //выдаем ее текущие параметры
                        result.current = part.current[blockFacing.Index];
                    }
                }

                foreach (var network in networks)
                {
                    result.NumberOfBlocks += network.PartPositions.Count;
                    result.NumberOfConsumers += network.Consumers.Count;
                    result.NumberOfProducers += network.Producers.Count;
                    result.NumberOfAccumulators += network.Accumulators.Count;
                    result.NumberOfTransformators += network.Transformators.Count;
                    result.Production += network.Production;
                    result.Consumption += network.Consumption;
                    result.Overflow += network.Overflow;
                    result.Request += network.Request;
                }
            }

            return result;
        }


    }









    /// <summary>
    /// Сам пакет с энергией
    /// </summary>
    public class EnergyPacket
    {
        /// <summary>
        /// Энергия, которая движется в этом пакете.
        /// </summary>
        public float energy;

        /// <summary>
        /// Напряжение, с которым движется энергия.
        /// </summary>
        public int voltage;

        /// <summary>
        /// Текущий индекс в пути, где сейчас пакет
        /// </summary>
        public int currentIndex = -1;

        /// <summary>
        /// Последовательность позиций по которой движется энергия.
        /// </summary>
        public readonly BlockPos[] path;

        /// <summary>
        /// Откуда мы пришли в каждой точке пути.
        /// </summary>
        public readonly int[] facingFrom;

        /// <summary>
        /// Какие грани каждого блока уже обработаны.
        /// </summary>
        public readonly bool[][] nowProcessedFaces;

        /// <summary>
        /// Через какие соединения шёл ток.
        /// </summary>
        public readonly Facing[] usedConnections;

        /// <summary>
        /// Создаёт пакет, просто сохраняя ссылки на массивы из кэша.
        /// </summary>
        public EnergyPacket(
            float Energy,
            int Voltage,
            int CurrentIndex,
            BlockPos[] Path,
            int[] FacingFrom,
            bool[][] NowProcessedFaces,
            Facing[] UsedConnections
        )
        {
            // Никаких Clone() — храним ссылку на кэшированные данные:
            energy = Energy;
            voltage = Voltage;
            currentIndex = CurrentIndex;
            path = Path;
            facingFrom = FacingFrom;
            nowProcessedFaces = NowProcessedFaces;
            usedConnections = UsedConnections;
        }


    }





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
            this.voltage = 0;
            this.maxCurrent = 0.0F;
            this.material = "";
            this.resisitivity = 0.0F;
            this.lines = 0;
            this.crossArea = 0.0F;
            this.burnout = false;
            this.isolated = false;
            this.isolatedEnvironment = true;
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
                int hash = 17;
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


    /// <summary>
    /// Сеть
    /// </summary>
    public class Network
    {
        public readonly HashSet<IElectricAccumulator> Accumulators = new();  //Аккумуляторы
        public readonly HashSet<IElectricConsumer> Consumers = new();       //Потребители
        public readonly HashSet<IElectricProducer> Producers = new();           //Генераторы
        public readonly HashSet<IElectricTransformator> Transformators = new();  //Трансформаторы
        public readonly HashSet<BlockPos> PartPositions = new();     //Координаты позиций сети
        public float Consumption; //Потребление
        public float Overflow;    //Переполнение
        public float Production;  //Генерация
        public float Request;        //Недостаток
        public int version; // Версия сети, для отслеживания изменений

    }

    /// <summary>
    /// Часть сети
    /// </summary>
    public class NetworkPart
    {
        public readonly Network?[] Networks = new Network?[6];
        public EParams[] eparams = new EParams[] { };
        public float[] current = new float[6];
        public readonly BlockPos Position;
        public Facing Connection = Facing.None;
        public IElectricAccumulator? Accumulator;
        public IElectricConsumer? Consumer;
        public IElectricProducer? Producer;
        public IElectricTransformator? Transformator;

        public NetworkPart(BlockPos position)
        {
            Position = position;
        }
    }


    /// <summary>
    /// Сборщик информации о сети
    /// </summary>
    public class NetworkInformation
    {
        public float Consumption;
        public float Overflow;
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