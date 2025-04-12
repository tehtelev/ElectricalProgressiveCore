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

[assembly: ModDependency("game", "1.20.0")]
[assembly: ModInfo(
    "Electrical Progressive: Core",
    "electricalprogressivecore",
    Website = "https://github.com/tehtelev/ElectricalProgressiveCore",
    Description = "Brings electricity into the game!",
    Version = "0.9.3",
    Authors = new[] { "Tehtelev", "Kotl" }
)]

namespace ElectricalProgressive
{
    public class ElectricalProgressive : ModSystem
    {
        private readonly List<Consumer> consumers = new();
        private readonly List<Producer> producers = new();
        private readonly List<Accumulator> accums = new();

        private readonly List<energyPacket> globalEnergyPackets = new(); // Глобальный список пакетов энергии

        private readonly List<BlockPos> consumerPositions = new();
        private readonly List<float> consumerRequests = new();
        private readonly List<BlockPos> producerPositions = new();
        private readonly List<float> producerGive = new();

        private readonly List<BlockPos> consumer2Positions = new();
        private readonly List<float> consumer2Requests = new();
        private readonly List<BlockPos> producer2Positions = new();
        private readonly List<float> producer2Give = new();

        private readonly HashSet<Network> networks = new();
        private readonly Dictionary<BlockPos, NetworkPart> parts = new(); // Хранит все элементы всех цепей
        public static bool combatoverhaul = false; // Установлен ли combatoverhaul
        public int speedOfElectricity; // Скорость электричества в проводах (блоков в тик)
        public bool instant; // Расчет мгновенно?
        private PathFinder pathFinder = new PathFinder(); // Модуль поиска путей
        private ICoreAPI api = null!;
        private ICoreClientAPI capi = null!;
        private ElectricityConfig config;


        /// <summary>
        /// Запуск модификации
        /// </summary>
        /// <param name="api"></param>
        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            this.api = api;
            api.Event.RegisterGameTickListener(this.OnGameTick, 500);

            if (api.ModLoader.IsModEnabled("combatoverhaul"))
                combatoverhaul = true;
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
            capi.Input.RegisterHotKey("AltPressForNetwork", Lang.Get("AltPressForNetworkName"), GlKeys.Unknown, HotkeyType.CharacterControls, true);
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

            Cleaner(false);
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
        public void Cleaner(bool all = false)
        {
            foreach (var network in networks)
            {
                foreach (var pos in network.PartPositions)
                {
                    if (parts[pos].eparams != null && parts[pos].eparams.Length > 0)
                    {
                        for (int i = 0; i < parts[pos].eparams.Length; i++)
                        {
                            if (parts[pos].eparams[i].Equals(new EParams()))
                                parts[pos].eparams[i] = new EParams();
                        }
                    }
                    else
                    {
                        parts[pos].eparams = new EParams[]
                        {
                            new EParams(), new EParams(), new EParams(),
                            new EParams(), new EParams(), new EParams()
                        };
                    }
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
        private void logisticalTask(Network network, List<BlockPos> consumerPositions, List<float> consumerRequests,
            List<BlockPos> producerPositions, List<float> producerGive, ref Simulation sim, out List<BlockPos>[][] paths,
            out List<int>[][] facingFrom, out List<bool[]>[][] nowProcessedFaces)
        {
            float[][] distances = new float[consumerPositions.Count][];
            paths = new List<BlockPos>[consumerPositions.Count][];
            facingFrom = new List<int>[consumerPositions.Count][];
            nowProcessedFaces = new List<bool[]>[consumerPositions.Count][];

            for (int i = 0; i < consumerPositions.Count; i++)
            {
                distances[i] = new float[producerPositions.Count];
                paths[i] = new List<BlockPos>[producerPositions.Count];
                facingFrom[i] = new List<int>[producerPositions.Count];
                nowProcessedFaces[i] = new List<bool[]>[producerPositions.Count];

                for (int j = 0; j < producerPositions.Count; j++)
                {
                    var key = (consumerPositions[i], producerPositions[j]);
                    if (network.pathCache.TryGetValue(key, out var entry) && entry.Version == network.version)
                    {
                        if (entry.Path != null)
                        {
                            paths[i][j] = entry.Path;
                            facingFrom[i][j] = entry.FacingFrom;
                            nowProcessedFaces[i][j] = entry.NowProcessedFaces;
                            distances[i][j] = entry.Path.Count;
                        }
                        else
                        {
                            distances[i][j] = float.MaxValue;
                        }
                    }
                    else
                    {
                        var (path, facing, processed) = pathFinder.FindShortestPath(consumerPositions[i], producerPositions[j], network, parts);
                        if (path != null)
                        {
                            paths[i][j] = path;
                            facingFrom[i][j] = facing;
                            nowProcessedFaces[i][j] = processed;
                            distances[i][j] = path.Count;
                            network.pathCache[key] = new PathCacheEntry
                            {
                                Path = path,
                                FacingFrom = facing,
                                NowProcessedFaces = processed,
                                Version = network.version
                            };
                        }
                        else
                        {
                            network.pathCache[key] = new PathCacheEntry { Version = network.version };
                            distances[i][j] = float.MaxValue;
                        }
                    }
                }
            }

            Store[] stores = new Store[producerPositions.Count];
            for (int j = 0; j < producerPositions.Count; j++)
                stores[j] = new Store(j + 1, producerGive[j]);

            Customer[] customers = new Customer[consumerPositions.Count];
            for (int i = 0; i < consumerPositions.Count; i++)
            {
                var distFromCustomerToStore = new Dictionary<Store, float>();
                for (int j = 0; j < producerPositions.Count; j++)
                    distFromCustomerToStore.Add(stores[j], distances[i][j]);
                customers[i] = new Customer(i + 1, consumerRequests[i], distFromCustomerToStore);
            }

            sim.Stores.AddRange(stores);
            sim.Customers.AddRange(customers);
            sim.Run();
        }




        /// <summary>
        /// Тикаем
        /// </summary>
        /// <param name="_"></param>
        private void OnGameTick(float _)
        {
            int ticks = instant ? 1 : speedOfElectricity;

            while (ticks >= 1)
            {
                Cleaner();

                foreach (var network in networks)
                {
                    // Этап 1: Очистка
                    producers.Clear();
                    consumers.Clear();
                    accums.Clear();
                    consumerPositions.Clear();
                    consumerRequests.Clear();
                    producerPositions.Clear();
                    producerGive.Clear();
                    consumer2Positions.Clear();
                    consumer2Requests.Clear();
                    producer2Positions.Clear();
                    producer2Give.Clear();

                    // Этап 2: Сбор запросов от потребителей
                    foreach (var electricConsumer in network.Consumers)
                    {
                        consumers.Add(new Consumer(electricConsumer));
                        float requestedEnergy = electricConsumer.Consume_request();
                        consumerPositions.Add(electricConsumer.Pos);
                        consumerRequests.Add(requestedEnergy);
                    }

                    // Этап 3: Сбор энергии с генераторов и аккумуляторов
                    foreach (var electricProducer in network.Producers)
                    {
                        producers.Add(new Producer(electricProducer));
                        float giveEnergy = electricProducer.Produce_give();
                        producerPositions.Add(electricProducer.Pos);
                        producerGive.Add(giveEnergy);
                    }

                    foreach (var electricAccum in network.Accumulators)
                    {
                        accums.Add(new Accumulator(electricAccum));
                        float giveEnergy = electricAccum.canRelease();
                        producerPositions.Add(electricAccum.Pos);
                        producerGive.Add(giveEnergy);
                    }

                    // Этап 4: Распределение энергии
                    var sim = new Simulation();
                    logisticalTask(network, consumerPositions, consumerRequests, producerPositions, producerGive,
                        ref sim, out var paths, out var facingFrom, out var nowProcessedFaces);

                    if (!instant) // Медленная передача
                    {
                        foreach (var customer in sim.Customers)
                        {
                            foreach (var store in sim.Stores)
                            {
                                if (customer.Received.TryGetValue(store, out var value))
                                {
                                    int indexStore = sim.Stores.IndexOf(store);
                                    BlockPos posStore = producerPositions[indexStore];
                                    int indexCustomer = sim.Customers.IndexOf(customer);

                                    var packet = new energyPacket
                                    {
                                        path = new List<BlockPos>(paths[indexCustomer][indexStore]),
                                        energy = value,
                                        voltage = parts[posStore].eparams[facingFrom[indexCustomer][indexStore].Last()].voltage,
                                        facingFrom = new List<int>(facingFrom[indexCustomer][indexStore]),
                                        nowProcessed = new List<bool[]>(nowProcessedFaces[indexCustomer][indexStore].Select(arr => arr.ToArray()))
                                    };
                                    globalEnergyPackets.Add(packet);
                                }
                            }
                        }
                    }

                    if (instant) // Мгновенная передача
                    {
                        int i = 0;
                        foreach (var consumer in consumers)
                        {
                            var totalGive = sim.Customers[i].Required - sim.Customers[i].Remaining;
                            consumer.ElectricConsumer.Consume_receive(totalGive);
                            i++;
                        }
                    }

                    // Этап 5: Забираем у аккумуляторов выданное
                    int j = 0;
                    foreach (var accum in accums)
                    {
                        if (sim.Stores[j + producers.Count].Stock < accum.ElectricAccum.canRelease())
                        {
                            accum.ElectricAccum.Release(accum.ElectricAccum.canRelease() - sim.Stores[j + producers.Count].Stock);
                        }
                        j++;
                    }

                    // Этап 6: Зарядка аккумуляторов
                    foreach (var accum in accums)
                    {
                        float requestedEnergy = accum.ElectricAccum.canStore();
                        consumer2Positions.Add(accum.ElectricAccum.Pos);
                        consumer2Requests.Add(requestedEnergy);
                    }

                    // Этап 7: Остатки генераторов
                    j = 0;
                    foreach (var producer in producers)
                    {
                        float giveEnergy = sim.Stores[j].Stock;
                        producer2Positions.Add(producer.ElectricProducer.Pos);
                        producer2Give.Add(giveEnergy);
                        j++;
                    }

                    // Этап 8: Распределение энергии для аккумуляторов
                    var sim2 = new Simulation();
                    logisticalTask(network, consumer2Positions, consumer2Requests, producer2Positions, producer2Give,
                        ref sim2, out var paths2, out var facingFrom2, out var nowProcessedFaces2);

                    if (!instant)
                    {
                        foreach (var customer in sim2.Customers)
                        {
                            foreach (var store in sim2.Stores)
                            {
                                if (customer.Received.TryGetValue(store, out var value))
                                {
                                    int indexStore = sim2.Stores.IndexOf(store);
                                    BlockPos posStore = producer2Positions[indexStore];
                                    int indexCustomer = sim2.Customers.IndexOf(customer);

                                    var packet = new energyPacket
                                    {
                                        path = new List<BlockPos>(paths2[indexCustomer][indexStore]),
                                        energy = value,
                                        voltage = parts[posStore].eparams[facingFrom2[indexCustomer][indexStore].Last()].voltage,
                                        facingFrom = new List<int>(facingFrom2[indexCustomer][indexStore]),
                                        nowProcessed = new List<bool[]>(nowProcessedFaces2[indexCustomer][indexStore].Select(arr => arr.ToArray()))
                                    };
                                    globalEnergyPackets.Add(packet);
                                }
                            }
                        }
                    }

                    if (instant)
                    {
                        j = 0;
                        foreach (var accum in accums)
                        {
                            var totalGive = sim2.Customers[j].Required - sim2.Customers[j].Remaining;
                            accum.ElectricAccum.Store(totalGive);
                            j++;
                        }
                    }

                    // Этап 9: Сообщение генераторам о нагрузке
                    j = 0;
                    foreach (var producer in producers)
                    {
                        var totalOrder = sim.Stores[j].totalRequest + sim2.Stores[j].totalRequest;
                        producer.ElectricProducer.Produce_order(totalOrder);
                        j++;
                    }

                    // Этап 10: Обновление статистики сети
                    network.Consumption = consumers.Sum(c => c.ElectricConsumer.getPowerReceive()) +
                                         accums.Sum(a => Math.Max(a.ElectricAccum.GetCapacity() - a.ElectricAccum.GetLastCapacity(), 0f));
                    network.Production = producers.Sum(p => Math.Min(p.ElectricProducer.getPowerGive(), p.ElectricProducer.getPowerOrder()));
                    network.Lack = Math.Max(consumers.Sum(c => c.ElectricConsumer.getPowerRequest() - c.ElectricConsumer.getPowerReceive()), 0);

                    accums.ForEach(a => a.ElectricAccum.Update());
                    producers.ForEach(p => p.ElectricProducer.Update());
                    consumers.ForEach(c => c.ElectricConsumer.Update());
                }



                if (!instant)
                {
                    // Этап 11: Потребление энергии пакетами

                    Dictionary<BlockPos, float> sumEnergy = new Dictionary<BlockPos, float>();
                    var copyg = new List<energyPacket>(globalEnergyPackets);
                    for (int i = copyg.Count - 1; i >= 0; i--)
                    {
                        var packet = copyg[i];
                        if (packet.path.Count == 1)
                        {
                            var pos = packet.path[0];
                            if (parts.TryGetValue(pos, out var part) &&
                                part.eparams.Where(s => s.voltage > 0).Any(s => packet.voltage >= s.voltage))
                            {
                                //суммируем все полученные пакеты данным потребителем
                                if (sumEnergy.TryGetValue(pos, out var value))
                                {
                                    sumEnergy[pos] += packet.energy;
                                }
                                else
                                {
                                    sumEnergy.Add(pos, packet.energy);
                                }
                            }

                            globalEnergyPackets.RemoveAt(i); //удаляем пакеты, которые не могут быть переданы дальше
                        }
                    }




                    //выдаем каждому потребителю сумму поглощенных пакетов
                    foreach (var part in parts)  //перебираем все элементы
                    {
                        if (!sumEnergy.ContainsKey(part.Key))    //если в этот тик потребители ничего не получили, то говорим даем им 0
                        {
                            sumEnergy.Add(part.Key, 0.0F);
                        }
                    }


                    foreach (var pair in sumEnergy)
                    {
                        if (parts[pair.Key].Consumer != null)
                            parts[pair.Key].Consumer.Consume_receive(pair.Value);
                        else if (parts[pair.Key].Accumulator != null)
                            parts[pair.Key].Accumulator.Store(pair.Value);
                    }


                    sumEnergy.Clear();

                    copyg.Clear();






                    // Этап 12: Перемещение пакетов
                    foreach (var part in parts)
                        part.Value.current = new float[6];


                    copyg = new List<energyPacket>(globalEnergyPackets);

                    for (int i = copyg.Count - 1; i >= 0; i--)
                    {
                        var packet = copyg[i];
                        if (packet.path.Count >= 2)
                        {
                            var currentPos = packet.path.Last();
                            var nextPos = packet.path[packet.path.Count - 2];
                            if (parts.TryGetValue(nextPos, out var nextPart) &&
                                pathFinder.ToGetNeighbor(currentPos, parts, packet.facingFrom.Last(), nextPos) &&
                                !nextPart.eparams[packet.facingFrom[packet.facingFrom.Count - 2]].burnout)
                            {
                                var currentPart = parts[currentPos];
                                float resistance = currentPart.eparams[packet.facingFrom.Last()].resisitivity /
                                                   (currentPart.eparams[packet.facingFrom.Last()].lines *
                                                    currentPart.eparams[packet.facingFrom.Last()].crossArea);
                                if (currentPart.eparams[packet.facingFrom.Last()].isolated)
                                    resistance /= 2.0f;

                                float current = packet.energy / packet.voltage;
                                float lossEnergy = current * current * resistance;
                                packet.energy = Math.Max(packet.energy - lossEnergy, 0);

                                if (packet.energy > 0.001f)
                                {
                                    packet.path.RemoveAt(packet.path.Count - 1);
                                    packet.facingFrom.RemoveAt(packet.facingFrom.Count - 1);
                                    packet.nowProcessed.RemoveAt(packet.nowProcessed.Count - 1);

                                    int j = 0;
                                    foreach (var face in packet.nowProcessed.Last())
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

                    copyg.Clear();





                    // Этап 13: Проверка сгорания проводов и трансформаторов
                    foreach (var part in parts)
                    {                        

                        if (part.Value.Transformator != null)
                        {
                            float energy = 0;
                            float current = 0;
                            for (int i = globalEnergyPackets.Count - 1; i >= 0; i--)
                            {
                                
                                if (globalEnergyPackets[i].path.Last() == part.Key)
                                {
                                    energy += globalEnergyPackets[i].energy;
                                    if (globalEnergyPackets[i].voltage == part.Value.Transformator.highVoltage)
                                        globalEnergyPackets[i].voltage = part.Value.Transformator.lowVoltage;
                                    else if (globalEnergyPackets[i].voltage == part.Value.Transformator.lowVoltage)
                                        globalEnergyPackets[i].voltage = part.Value.Transformator.highVoltage;
                                    current += globalEnergyPackets[i].energy / globalEnergyPackets[i].voltage;
                                }
                            }

                            part.Value.current[5] = current;
                            part.Value.Transformator.setPower(energy);
                            part.Value.Transformator.Update();
                        }
                    }



                    copyg = new List<energyPacket>(globalEnergyPackets);

                    foreach (var part in parts)
                    {
                        for (int j = copyg.Count - 1; j >= 0; j--)
                        {
                            //пакет превышает напряжение проводника и находится на этой грани?
                            if (copyg[j].path.Last() == part.Key && part.Value.eparams[copyg[j].facingFrom.Last()].voltage != 0 && copyg[j].voltage > part.Value.eparams[copyg[j].facingFrom.Last()].voltage)
                            {
                                parts[part.Key].eparams[copyg[j].facingFrom.Last()].burnout = true; //проводок сгорел на этой грани

                                var removedFace = FacingHelper.FromFace(FacingHelper.BlockFacingFromIndex(copyg[j].facingFrom.Last()));

                                parts.TryGetValue(part.Key, out var part2);
                                part2.Connection &= ~removedFace; //вычитаем по сути эти соединения

                                RemoveConnections(ref part2, removedFace);  // убираем соединение

                                //уничтожаем все пакеты в этой точке грани
                                globalEnergyPackets.RemoveAt(j);

                                //обновляем и обнуляем сгоревшие приборы
                                if (part.Value.Consumer != null)
                                {
                                    parts[part.Key].Consumer!.Consume_receive(0.0F); //потребитель не получил энергию
                                    parts[part.Key].Consumer!.Update(); //обновляем потребителя
                                    parts[part.Key].Consumer = null; //обнуляем потребителя
                                }
                                else if (part.Value.Producer != null)
                                {
                                    parts[part.Key].Producer!.Produce_order(0.0F); //генератор не выдал энергию
                                    parts[part.Key].Producer!.Update(); //обновляем генератор
                                    parts[part.Key].Producer = null; //обнуляем генератор
                                }
                                else if (part.Value.Accumulator != null)
                                {
                                    parts[part.Key].Accumulator!.SetCapacity(0.0F); //аккумулятор не принял энергию
                                    parts[part.Key].Accumulator!.Update(); //обновляем аккумулятор
                                    parts[part.Key].Accumulator = null; //обнуляем аккумулятор
                                }
                                else if (part.Value.Transformator != null)
                                {
                                    parts[part.Key].Transformator!.setPower(0.0F); //трансформатор не принял энергию
                                    parts[part.Key].Transformator!.Update(); //обновляем трансформатор
                                    parts[part.Key].Transformator = null; //обнуляем трансформатор
                                }

                            }
                        }

                        copyg.Clear();

                        copyg = new List<energyPacket>(globalEnergyPackets);

                        for (int i = 0; i < 6; i++)
                        {
                            if (part.Value.eparams[i].voltage != 0 && part.Value.current[i] > part.Value.eparams[i].maxCurrent * part.Value.eparams[i].lines)
                            {
                                part.Value.eparams[i].burnout = true;
                                var removedFace = FacingHelper.FromFace(FacingHelper.BlockFacingFromIndex(i));
                                this.parts.TryGetValue(part.Key, out var part2);
                                part2.Connection &= ~removedFace;
                                RemoveConnections(ref part2, removedFace);

                                for (int j = copyg.Count - 1; j >= 0; j--)
                                {
                                    if (copyg[j].path.Last() == part.Key &&
                                        copyg[j].nowProcessed.Last()[i])
                                    {
                                        globalEnergyPackets.RemoveAt(j);
                                    }
                                }

                                if (part.Value.Consumer != null)
                                {
                                    part.Value.Consumer.Consume_receive(0);
                                    part.Value.Consumer.Update();
                                    part.Value.Consumer = null;
                                }
                                else if (part.Value.Producer != null)
                                {
                                    part.Value.Producer.Produce_order(0);
                                    part.Value.Producer.Update();
                                    part.Value.Producer = null;
                                }
                                else if (part.Value.Accumulator != null)
                                {
                                    part.Value.Accumulator.SetCapacity(0);
                                    part.Value.Accumulator.Update();
                                    part.Value.Accumulator = null;
                                }
                                else if (part.Value.Transformator != null)
                                {
                                    part.Value.Transformator.setPower(0);
                                    part.Value.Transformator.Update();
                                    part.Value.Transformator = null;
                                }
                            }
                        }
                    }



                 




                    copyg.Clear();
                }

                ticks--;
            }
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
            outNetwork.version++; // Увеличиваем версию после слияния
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
                        if ((neighborPart.Connection & FacingHelper.From(face, direction.Opposite)) != 0)
                        {
                            if (neighborPart.Networks[face.Index] is { } network)
                            {
                                networksByFace[face.Index].Add(network);
                            }
                        }

                        if ((neighborPart.Connection & FacingHelper.From(direction.Opposite, face)) != 0)
                        {
                            if (neighborPart.Networks[direction.Opposite.Index] is { } network)
                            {
                                networksByFace[face.Index].Add(network);
                            }
                        }
                    }
                }
            }

            //поиск соседей по ребрам
            foreach (var direction in FacingHelper.Directions(addedConnections))
            {
                var directionFilter = FacingHelper.FromDirection(direction);

                foreach (var face in FacingHelper.Faces(addedConnections & directionFilter))
                {
                    var neighborPosition = part.Position.AddCopy(direction).AddCopy(face);

                    if (this.parts.TryGetValue(neighborPosition, out var neighborPart))
                    {
                        if ((neighborPart.Connection & FacingHelper.From(direction.Opposite, face.Opposite)) != 0)
                        {
                            if (neighborPart.Networks[direction.Opposite.Index] is { } network)
                            {
                                networksByFace[face.Index].Add(network);
                            }
                        }

                        if ((neighborPart.Connection & FacingHelper.From(face.Opposite, direction.Opposite)) != 0)
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




        private void RemoveConnections(ref NetworkPart part, Facing removedConnections)
        {


            foreach (var blockFacing in FacingHelper.Faces(removedConnections))
            {
                if (part.Networks[blockFacing.Index] is { } network)
                {
                    this.RemoveNetwork(ref network);
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
                    result.Lack += network.Lack;
                }
            }

            return result;
        }


    }


    /// <summary>
    /// Сам пакет с энергией
    /// </summary>
    public class energyPacket
    {
        public List<BlockPos> path = new();
        public float energy;
        public int voltage;
        public List<int> facingFrom = new();
        public List<bool[]> nowProcessed = new();
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
        public bool isolated;       //изолированный провод

        public EParams(int voltage, float maxCurrent, string material, float resisitivity, byte lines, float crossArea, bool burnout, bool isolated)
        {
            this.voltage = voltage;
            this.maxCurrent = maxCurrent;
            this.material = material;
            this.resisitivity = resisitivity;
            this.lines = lines;
            this.crossArea = crossArea;
            this.burnout = burnout;
            this.isolated = isolated;
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
                   isolated == other.isolated;
        }

        public override bool Equals(object obj)
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
                return hash;
            }
        }
    }

    public class Network
    {
        public readonly HashSet<IElectricAccumulator> Accumulators = new();
        public readonly HashSet<IElectricConsumer> Consumers = new();
        public readonly HashSet<IElectricProducer> Producers = new();
        public readonly HashSet<IElectricTransformator> Transformators = new();
        public readonly HashSet<BlockPos> PartPositions = new();
        public float Consumption;
        public float Overflow;
        public float Production;
        public float Lack;
        public int version = 0;
        public Dictionary<(BlockPos, BlockPos), PathCacheEntry> pathCache = new();
    }

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

    public class NetworkInformation
    {
        public float Consumption;
        public float Overflow;
        public float Production;
        public float Lack;
        public Facing Facing = Facing.None;
        public int NumberOfAccumulators;
        public int NumberOfBlocks;
        public int NumberOfConsumers;
        public int NumberOfProducers;
        public int NumberOfTransformators;
        public EParams eParamsInNetwork = new();
        public float current;
    }

    internal class Consumer
    {
        public readonly IElectricConsumer ElectricConsumer;
        public Consumer(IElectricConsumer electricConsumer) => ElectricConsumer = electricConsumer;
    }

    internal class Producer
    {
        public readonly IElectricProducer ElectricProducer;
        public Producer(IElectricProducer electricProducer) => ElectricProducer = electricProducer;
    }

    internal class Accumulator
    {
        public readonly IElectricAccumulator ElectricAccum;
        public Accumulator(IElectricAccumulator electricAccum) => ElectricAccum = electricAccum;
    }

    public class ElectricityConfig
    {
        public int speedOfElectricity = 2;
        public bool instant = false;
    }

    public struct PathCacheEntry
    {
        public List<BlockPos>? Path;
        public List<int>? FacingFrom;
        public List<bool[]>? NowProcessedFaces;
        public int Version;
    }
}