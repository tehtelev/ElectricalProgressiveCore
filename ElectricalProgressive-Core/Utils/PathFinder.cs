using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;


namespace ElectricalProgressive.Utils;

public class PathFinder
{
    /// <summary>
    /// Конструктор для поиска пути в сети
    /// </summary>
    /// <param name="net"></param>
    /// <param name="partss"></param>
    public PathFinder(Network net, Dictionary<BlockPos, NetworkPart> partss)
    {
        network = net;
        parts = partss;
    }

    /// <summary>
    /// Сеть, в которой мы ищем путь
    /// </summary>
    private Network network;

    /// <summary>
    /// Словарь частей сети
    /// </summary>
    private Dictionary<BlockPos, NetworkPart> parts;


    /// <summary>
    /// Эвристическая функция (манхэттенское расстояние)
    /// </summary>
    private static int Heuristic(BlockPos a, BlockPos b)
    {
        return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z);
    }


    /// <summary>
    /// Массив масок граней для фильтрации соединений
    /// </summary>
    private static Facing[] faceMasks =
    {
        Facing.NorthAll, Facing.EastAll, Facing.SouthAll, Facing.WestAll, Facing.UpAll, Facing.DownAll
    };



    // Переменные используемые в GetNeighbors, чтобы избежать очень частых аллокаций
    private List<BlockPos> Neighbors = new();      // координата соседа
    private List<int> NeighborsFace = new();            // грань соседа с которым мы взаимодействовать будем
    private bool[] NowProcessed = new bool[6];                    // задействованные грани в этой точке
    private Queue<int> queue2 = new();
    private bool[] processFacesBuf = new bool[6];
    private BlockPos neighborPosition;
    private List<BlockFacing> bufForDirections = new List<BlockFacing>(6);
    private List<BlockFacing> bufForFaces = new List<BlockFacing>(6);

    // Переменные используемые в FindShortestPath, чтобы избежать очень частых аллокаций
    private List<int> startBlockFacing = new();
    private List<int> endBlockFacing = new();
    private PriorityQueue<(BlockPos, int), int> queue = new();
    private Dictionary<(BlockPos, int), (BlockPos, int)> cameFrom = new();
    private List<BlockPos> cameFromList = new();
    private Dictionary<BlockPos, bool[]> processedFaces = new();
    private Dictionary<(BlockPos, int), int> facingFrom = new();
    private Dictionary<(BlockPos, int), bool[]> nowProcessedFaces = new();
    private HashSet<BlockPos> networkPositions = new();
    private List<BlockPos> buf1 = new();     //список соседей
    private List<int> buf2 = new();          //список граней соседей
    private bool[] buf3;            //список граней, которые сейчас в работе
    private bool[] buf4;            //список граней, которые уже просчитаны
    private BlockPos currentPos;    //текущая позиция
    private int currentFace;        //текущая грань





    /// <summary>
    /// Ищет кратчайший путь от начальной позиции к конечной в сети
    /// </summary>
    /// <param name="start"></param>
    /// <param name="end"></param>
    /// <returns></returns>
    public (BlockPos[], int[], bool[][], Facing[]) FindShortestPath(BlockPos start, BlockPos end)
    {
        // очищаем предыдущие данные
        startBlockFacing.Clear();
        endBlockFacing.Clear();
        queue.Clear();
        cameFrom.Clear();
        cameFromList.Clear();
        facingFrom.Clear();
        nowProcessedFaces.Clear();
        processedFaces.Clear();
        buf1.Clear();
        buf2.Clear();
        buf3 = Array.Empty<bool>();
        buf4 = Array.Empty<bool>();





        networkPositions = network.PartPositions; // ни в коем случае не очищать

        //проверяем наличие начальной и конечной точки в этой цепи
        if (!networkPositions.Contains(start) || !networkPositions.Contains(end))
            return (null!, null!, null!, null!);


        //смотрим с какой грани начинать
        var startConnection = parts[start].Connection;
        foreach (var face in FacingHelper.Faces(startConnection))
        {
            startBlockFacing.Add(face.Index);
        }


        // startBlockFacing[0] и endBlockFacing[0] будут работать корректно до тех пор, пока не появятся источники и приемники энергии, у которых несколько граней на передачу и прием!!!!



        //смотрим с какой грани заканчивать
        var endConnection = parts[end].Connection;
        foreach (var face in FacingHelper.Faces(endConnection))
        {
            endBlockFacing.Add(face.Index);
        }

        //очередь обработки

        queue.Enqueue((start, startBlockFacing[0]),0);

        //хранит цепочку пути и грань

        cameFrom[(start, startBlockFacing[0])] = (null!, 0);

        //хранит цепочку пути (для вывода наружу)
        cameFromList.Add(start);

        //хранит номер задействованной грани соседа 

        facingFrom[(start, startBlockFacing[0])] = startBlockFacing[0];


        //хранит для каждого кусочка цепи посещенные грани в данный момент

        nowProcessedFaces[(start, startBlockFacing[0])] = new bool[6] { false, false, false, false, false, false };
        nowProcessedFaces[(start, startBlockFacing[0])][startBlockFacing[0]] = true;




        // хранит для каждого кусочка цепи все посещенные грани
        // словарь не перезаполняется, а лишь очищается при каждом новом запуске поиска пути для той же сети, чтобы не создавать новые объекты
        foreach (var index in networkPositions)
        {
            if (!processedFaces.TryGetValue(index, out _))
            {
                processedFaces.Add(index, new bool[6] { false, false, false, false, false, false });
            }
            else
            {
                processedFaces[index][0] = false;
                processedFaces[index][1] = false;
                processedFaces[index][2] = false;
                processedFaces[index][3] = false;
                processedFaces[index][4] = false;
                processedFaces[index][5] = false;
            }

        }



        while (queue.Count > 0)                 //пока очередь не опустеет
        {
            (currentPos, currentFace) = queue.Dequeue();

            if (currentPos.Equals(end))            //достигли конца и прекращаем просчет
                break;


            // Затем используйте распаковку:
            (buf1, buf2, buf3, buf4) = GetNeighbors(currentPos, processedFaces[currentPos], facingFrom[(currentPos, currentFace)]);



            processedFaces[currentPos] = buf4;    //обновляем информацию о всех просчитанных гранях     

            int i = 0;
            foreach (var neighbor in buf1)
            {
                var state = (neighbor, buf2[i]);
                int priority = Heuristic(neighbor, end); // Приоритет = эвристика
                if (!processedFaces[neighbor][buf2[i]]   // проверяем, что грань соседа еще не обработана
                    && !cameFrom.ContainsKey(state)      // проверяем, что состояние еще не посещали
                    && priority<200)                     // ограничение на приоритет, чтобы не зацикливаться на бесконечном поиске
                {
                    
                    queue.Enqueue(state, priority);

                    cameFrom[state] = (currentPos, facingFrom[(currentPos, currentFace)]);
                    cameFromList.Add(neighbor);

                    facingFrom[state] = buf2[i];

                    nowProcessedFaces[state] = buf3;
                }

                i++;
            }

            //if (cameFrom.Count > 1000)
            //{ // Ограничение на количество посещенных состояний
            //    return (null!, null!, null!, null!);
            //}

        }

        if (!cameFromList.Contains(end))        //не нашли конец?
            return (null!, null!, null!, null!);

        var (path, faces) = ReconstructPath(start, end, endBlockFacing[0], cameFrom);    //реконструкция маршрута


        // Если путь не найден, возвращаем null
        if (path == null)
            return (null!, null!, null!, null!);

        int pathLength = path.Length;
        var nowProcessingFaces = new Facing[pathLength];
        var nowProcessedFacesList = new bool[pathLength][];
        var facingFromList = new int[pathLength];

        // Заполняем массивы с информацией о гранях и состоянии
        for (int i = 0; i < pathLength; i++)
        {
            var state = (path[i], faces[i]);
            facingFromList[i] = facingFrom[state];
            nowProcessedFacesList[i] = nowProcessedFaces.ContainsKey(state)
                ? nowProcessedFaces[state]
                : new bool[6];

            var partConn = parts[path[i]].Connection;
            Facing result = Facing.None;
            var npf = nowProcessedFacesList[i];
            if (npf[0]) result |= partConn & Facing.NorthAll;
            if (npf[1]) result |= partConn & Facing.EastAll;
            if (npf[2]) result |= partConn & Facing.SouthAll;
            if (npf[3]) result |= partConn & Facing.WestAll;
            if (npf[4]) result |= partConn & Facing.UpAll;
            if (npf[5]) result |= partConn & Facing.DownAll;
            nowProcessingFaces[i] = result;
        }

        return (path, facingFromList, nowProcessedFacesList, nowProcessingFaces);
    }





    /// <summary>
    /// Вычисляет позиции соседей от текущего значения
    /// </summary>
    /// <param name="pos"></param>
    /// <returns></returns>
    private (List<BlockPos>, List<int>, bool[], bool[]) GetNeighbors(BlockPos pos, bool[] processFaces, int startFace)
    {
        // очищаем предыдущие данные
        Neighbors.Clear();                                // координата соседа
        NeighborsFace.Clear();                            // грань соседа с которым мы взаимодействовать будем
        NowProcessed.Fill(false);                    // задействованные грани в этой точке
        queue2.Clear();
        processFacesBuf.Fill(false);


        var part = parts[pos];                                // текущий элемент
        var Connections = part.Connection;                    // соединения этого элемента


        Facing hereConnections = Facing.None;

        // выясняем какие грани соединены с сетью, не сгорели, или не обработаны еще
        for (int i = 0; i < 6; i++)
        {
            if (part.Networks[i] == network && !part.eparams[i].burnout && !processFaces[i])
            {
                hereConnections |= Connections & faceMasks[i];
            }
        }

        // выясняем с какой гранью мы работаем и соединены ли грани одной цепи
        int startFaceIndex = startFace;
        queue2.Enqueue(startFaceIndex);


        processFaces.CopyTo(processFacesBuf, 0);
        processFacesBuf[startFaceIndex] = true;

        // Поиск всех связанных граней
        while (queue2.Count > 0)
        {
            int currentFaceIndex = queue2.Dequeue();
            BlockFacing currentFace = FacingHelper.BlockFacingFromIndex(currentFaceIndex);
            Facing currentFaceMask = FacingHelper.FromFace(currentFace);
            Facing connections = hereConnections & currentFaceMask;

            FacingHelper.FillDirections(connections, bufForDirections);
            foreach (var direction in bufForDirections)
            {
                int targetFaceIndex = direction.Index;

                if (!processFacesBuf[targetFaceIndex] && (hereConnections & FacingHelper.From(direction, currentFace)) != 0)
                {
                    processFacesBuf[targetFaceIndex] = true;
                    queue2.Enqueue(targetFaceIndex);
                }
            }
        }

        // Обновляем hereConnections, оставляя только связи найденных граней
        Facing validConnectionsMask = Facing.None;
        for (int i = 0; i < 6; i++)
        {
            if (processFacesBuf[i])
            {
                validConnectionsMask |= FacingHelper.FromFace(FacingHelper.BlockFacingFromIndex(i));
            }
        }
        hereConnections &= validConnectionsMask;


        // ищем соседей везде
        FacingHelper.FillDirections(hereConnections, bufForDirections);
        foreach (var direction in bufForDirections)
        {
            // ищем соседей по граням
            var directionFilter = FacingHelper.FromDirection(direction);
            neighborPosition = part.Position.AddCopy(direction);

            if (parts.TryGetValue(neighborPosition, out var neighborPart))
            {
                FacingHelper.FillFaces(hereConnections & directionFilter, bufForFaces);
                foreach (var face in bufForFaces)
                {
                    var opposite = direction.Opposite;

                    if ((neighborPart.Connection & FacingHelper.From(face, opposite)) != 0)
                    {
                        Neighbors.Add(neighborPosition);
                        NeighborsFace.Add(face.Index);
                        NowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }

                    if ((neighborPart.Connection & FacingHelper.From(opposite, face)) != 0)
                    {
                        Neighbors.Add(neighborPosition);
                        NeighborsFace.Add(opposite.Index);
                        NowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }
                }
            }

            // ищем соседей по ребрам
            directionFilter = FacingHelper.FromDirection(direction);

            FacingHelper.FillFaces(hereConnections & directionFilter, bufForFaces);
            foreach (var face in bufForFaces)
            {
                neighborPosition = part.Position.AddCopy(direction).AddCopy(face);

                if (parts.TryGetValue(neighborPosition, out neighborPart))
                {
                    var oppDir = direction.Opposite;
                    var oppFace = face.Opposite;

                    if ((neighborPart.Connection & FacingHelper.From(oppDir, oppFace)) != 0)
                    {
                        Neighbors.Add(neighborPosition);
                        NeighborsFace.Add(oppDir.Index);
                        NowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }

                    if ((neighborPart.Connection & FacingHelper.From(oppFace, oppDir)) != 0)
                    {
                        Neighbors.Add(neighborPosition);
                        NeighborsFace.Add(oppFace.Index);
                        NowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }
                }
            }


            // ищем соседей по перпендикулярной грани
            directionFilter = FacingHelper.FromDirection(direction);

            FacingHelper.FillFaces(hereConnections & directionFilter, bufForFaces);
            foreach (var face in bufForFaces)
            {
                neighborPosition = part.Position.AddCopy(face);

                if (parts.TryGetValue(neighborPosition, out neighborPart))
                {
                    var oppFace = face.Opposite;

                    if ((neighborPart.Connection & FacingHelper.From(direction, oppFace)) != 0)
                    {
                        Neighbors.Add(neighborPosition);
                        NeighborsFace.Add(direction.Index);
                        NowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }

                    if ((neighborPart.Connection & FacingHelper.From(oppFace, direction)) != 0)
                    {
                        Neighbors.Add(neighborPosition);
                        NeighborsFace.Add(oppFace.Index);
                        NowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }
                }

            }
        }



        return (Neighbors, NeighborsFace, NowProcessed, processFaces);
    }






    /// <summary>
    /// Реконструирует маршрут
    /// </summary>
    /// <param name="start"></param>
    /// <param name="end"></param>
    /// <param name="endFacing"></param>
    /// <param name="cameFrom"></param>
    /// <returns></returns>
    private (BlockPos[]? path, int[]? faces) ReconstructPath(
        BlockPos start,
        BlockPos end,
        int endFacing,
        Dictionary<(BlockPos, int), (BlockPos, int)> cameFrom)
    {
        // 1) Первый проход: считаем длину пути
        int length = 0;
        var current = (pos: end, facing: endFacing);

        while (current.pos != null)
        {
            length++;
            // пытаемся перейти к предку; если не можем — значит путь неполный
            if (!cameFrom.TryGetValue(current, out current))
                return (null, null);
        }

        // 2) Аллокация массивов ровно под нужный размер
        var pathArray = new BlockPos[length];
        var faceArray = new int[length];

        // 3) Второй проход: заполняем массивы с конца в начало
        current = (end, endFacing);
        for (int i = length - 1; i >= 0; i--)
        {
            pathArray[i] = current.pos;
            faceArray[i] = current.facing;
            // при последней итерации (i == 0) попытка провалится, но нам уже не нужен следующий элемент
            cameFrom.TryGetValue(current, out current);
        }

        // 4) Проверяем, что начало пути совпадает со стартовой точкой
        return pathArray[0].Equals(start)
            ? (pathArray, faceArray)
            : (null, null);
    }


}