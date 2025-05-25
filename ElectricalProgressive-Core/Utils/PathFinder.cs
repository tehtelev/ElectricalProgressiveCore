using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.MathTools;


namespace ElectricalProgressive.Utils;

public class PathFinder
{

    /// <summary>
    /// Реализует обход в ширину для поиска кратчайшего пути
    /// </summary>
    /// <param name="start"></param>
    /// <param name="end"></param>
    /// <param name="networkPositions"></param>
    /// <returns></returns>
    public (BlockPos[], int[], bool[][], Facing[]) FindShortestPath(BlockPos start, BlockPos end, Network network, Dictionary<BlockPos, NetworkPart> parts)
    {

        //проверяем наличие начальной и конечной точки в этой цепи
        var networkPositions = network.PartPositions;
        if (!networkPositions.Contains(start) || !networkPositions.Contains(end))
            return (null!, null!, null!, null!);


        //смотрим с какой грани начинать
        var startConnection = parts[start].Connection;
        var startBlockFacing = new List<int>();
        foreach (var face in FacingHelper.Faces(startConnection))
        {
            startBlockFacing.Add(face.Index);
        }

        //смотрим с какой грани заканчивать
        var endConnection = parts[end].Connection;
        var endBlockFacing = new List<int>();
        foreach (var face in FacingHelper.Faces(endConnection))
        {
            endBlockFacing.Add(face.Index);
        }

        //очередь обработки
        var queue = new Queue<(BlockPos, int)>();
        queue.Enqueue((start, startBlockFacing[0]));

        //хранит цепочку пути и грань
        var cameFrom = new Dictionary<(BlockPos, int), (BlockPos, int)>();
        cameFrom[(start, startBlockFacing[0])] = (null!, 0);

        //хранит цепочку пути (для вывода наружу)
        var cameFromList = new List<BlockPos>();
        cameFromList.Add(start);

        //хранит номер задействованной грани соседа 
        var facingFrom = new Dictionary<(BlockPos, int), int>();
        facingFrom[(start, startBlockFacing[0])] = startBlockFacing[0];


        //хранит для каждого кусочка цепи посещенные грани в данный момент
        var nowProcessedFaces = new Dictionary<(BlockPos, int), bool[]>();
        nowProcessedFaces[(start, startBlockFacing[0])] = new bool[6] { false, false, false, false, false, false };
        nowProcessedFaces[(start, startBlockFacing[0])][startBlockFacing[0]] = true;




        //хранит для каждого кусочка цепи все посещенные грани
        var processedFaces = new Dictionary<BlockPos, bool[]>();
        foreach (var index in networkPositions)
        {
            processedFaces[index] = new bool[6] { false, false, false, false, false, false };
        }

        bool first = true;                      //маркер для первого прохода
                                           
        List<BlockPos> buf1;     //список соседей
        List<int> buf2;          //список граней соседей
        bool[] buf3;            //список граней, которые сейчас в работе
        bool[] buf4;            //список граней, которые уже просчитаны
        BlockPos currentPos;    //текущая позиция
        int currentFace;        //текущая грань

        while (queue.Count > 0)                 //пока очередь не опустеет
        {
            (currentPos, currentFace) = queue.Dequeue();

            if (currentPos.Equals(end))            //достигли конца и прекращаем просчет
                break;


            // Затем используйте распаковку:
            (buf1, buf2, buf3, buf4) = GetNeighbors(currentPos, network, parts, first, processedFaces[currentPos], facingFrom[(currentPos, currentFace)]);



            processedFaces[currentPos] = buf4;    //обновляем информацию о всех просчитанных гранях     

            int i = 0;
            foreach (var neighbor in buf1)
            {
                if (!processedFaces[neighbor][buf2[i]] && !cameFrom.ContainsKey((neighbor, buf2[i])))  //если соседская грань уже учавствовала в расчете, то пропускаем этого соседа
                {
                    queue.Enqueue((neighbor, buf2[i]));

                    cameFrom[(neighbor, buf2[i])] = (currentPos, facingFrom[(currentPos, currentFace)]);
                    cameFromList.Add(neighbor);

                    facingFrom[(neighbor, buf2[i])] = buf2[i];

                    nowProcessedFaces[(neighbor, buf2[i])] = buf3;
                }

                i++;
            }


            first = false; //сбросили маркер
        }

        if (!cameFromList.Contains(end))        //не нашли конец?
            return (null!, null!, null!, null!);

        var (path, faces) = ReconstructPath(start, end, endBlockFacing[0], cameFrom);    //реконструкция маршрута

        Facing[] nowProcessingFaces=null!;      //храним тут Facing граней, которые сейчас в работе                                           
        bool[][] nowProcessedFacesList = null!; //хранит для каждого кусочка цепи посещенные грани в данный момент (для вывода наружу)                                                
        int[] facingFromList = null!;            //хранит номер задействованной грани соседа (для вывода наружу)

        if (path != null)
        {
            bool[] npf;    
            Facing facing;
            nowProcessingFaces = new Facing[path.Count()]; 
            nowProcessedFacesList = new bool[path.Count()][];
            facingFromList = new int[path.Count()];

            for (int i = 0; i < path.Count(); i++)                                //подготавливаем дополнительные данные
            {
                facingFromList[i]=facingFrom[(path[i], faces[i])];

                npf = nowProcessedFaces[(path[i], faces[i])];

                nowProcessedFacesList[i]= npf;

                //фильтруем только нужные грани
                facing = parts[path[i]].Connection &
                    ( (npf[0] ? Facing.NorthAll : Facing.None)
                    | (npf[1] ? Facing.EastAll : Facing.None)
                    | (npf[2] ? Facing.SouthAll : Facing.None)
                    | (npf[3] ? Facing.WestAll : Facing.None)
                    | (npf[4] ? Facing.UpAll : Facing.None)
                    | (npf[5] ? Facing.DownAll : Facing.None));

                nowProcessingFaces[i]=facing;
            }
        }


        return (path, facingFromList, nowProcessedFacesList, nowProcessingFaces);
    }



    /// <summary>
    /// Вычисляет позиции соседей от текущего значения
    /// </summary>
    /// <param name="pos"></param>
    /// <param name="network"></param>
    /// <returns></returns>
    private (List<BlockPos>, List<int>, bool[], bool[]) GetNeighbors(BlockPos pos, Network network, Dictionary<BlockPos, NetworkPart> parts, bool first, bool[] processFaces, int startFace)
    {
        var networkPositions = network.PartPositions; // позиции в этой сети

        List<BlockPos> Neighbors = new List<BlockPos>();      // координата соседа
        List<int> NeighborsFace = new List<int>();            // грань соседа с которым мы взаимодействовать будем
        bool[] NowProcessed = new bool[6];                    // задействованные грани в этой точке

        var part = parts[pos];                                // текущий элемент
        var Connections = part.Connection;                    // соединения этого элемента

        bool[] faces = new bool[6]; // фильтрация по текущей сети и не сгоревшим граням

        for (int i = 0; i < 6; i++)
        {
            if (part.Networks[i] == network && !part.eparams[i].burnout)
            {
                faces[i] = true;
            }
        }

        Facing hereConnections = Facing.None;
        Facing[] faceMasks = new Facing[]
        {
        Facing.NorthAll, Facing.EastAll, Facing.SouthAll, Facing.WestAll, Facing.UpAll, Facing.DownAll
        };

        for (int i = 0; i < 6; i++)
        {
            if (faces[i] && !processFaces[i])
            {
                hereConnections |= Connections & faceMasks[i];
            }
        }

        // выясняем с какой гранью мы работаем и соединены ли грани одной цепи
        int startFaceIndex = startFace;
        var queue = new Queue<int>();
        queue.Enqueue(startFaceIndex);

        bool[] processFacesBuf = new bool[6];
        processFaces.CopyTo(processFacesBuf, 0);
        processFacesBuf[startFaceIndex] = true;

        // Поиск всех связанных граней
        while (queue.Count > 0)
        {
            int currentFaceIndex = queue.Dequeue();
            BlockFacing currentFace = FacingHelper.BlockFacingFromIndex(currentFaceIndex);
            Facing currentFaceMask = FacingHelper.FromFace(currentFace);
            Facing connections = hereConnections & currentFaceMask;

            foreach (BlockFacing direction in FacingHelper.Directions(connections))
            {
                int targetFaceIndex = direction.Index;

                if (!processFacesBuf[targetFaceIndex] && (hereConnections & FacingHelper.From(direction, currentFace)) != 0)
                {
                    processFacesBuf[targetFaceIndex] = true;
                    queue.Enqueue(targetFaceIndex);
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

        // ищем соседей по граням
        foreach (var direction in FacingHelper.Directions(hereConnections))
        {
            var directionFilter = FacingHelper.FromDirection(direction);
            var neighborPosition = part.Position.AddCopy(direction);

            if (!parts.TryGetValue(neighborPosition, out var neighborPart)) continue;
            if (!networkPositions.Contains(neighborPosition)) continue;

            foreach (var face in FacingHelper.Faces(hereConnections & directionFilter))
            {
                var opposite = direction.Opposite;

                if ((neighborPart.Connection & FacingHelper.From(face, opposite)) != 0 ||
                    (neighborPart.Connection & FacingHelper.From(opposite, face)) != 0)
                {
                    Neighbors.Add(neighborPosition);
                    NeighborsFace.Add(face.Index);
                    NowProcessed[face.Index] = true;
                    processFaces[face.Index] = true;
                }
            }
        }

        // ищем соседей по ребрам
        foreach (var direction in FacingHelper.Directions(hereConnections))
        {
            var directionFilter = FacingHelper.FromDirection(direction);

            foreach (var face in FacingHelper.Faces(hereConnections & directionFilter))
            {
                var neighborPosition = part.Position.AddCopy(direction).AddCopy(face);

                if (!parts.TryGetValue(neighborPosition, out var neighborPart)) continue;
                if (!networkPositions.Contains(neighborPosition)) continue;

                var oppDir = direction.Opposite;
                var oppFace = face.Opposite;

                if ((neighborPart.Connection & FacingHelper.From(oppDir, oppFace)) != 0 ||
                    (neighborPart.Connection & FacingHelper.From(oppFace, oppDir)) != 0)
                {
                    Neighbors.Add(neighborPosition);
                    NeighborsFace.Add(oppDir.Index);
                    NowProcessed[face.Index] = true;
                    processFaces[face.Index] = true;
                }
            }
        }

        return (Neighbors, NeighborsFace, NowProcessed, processFaces);
    }



    /*
    /// <summary>
    /// Вычисляет позиции соседей от текущего значения
    /// </summary>
    /// <param name="pos"></param>
    /// <param name="network"></param>
    /// <returns></returns>
    public bool ToGetNeighbor(BlockPos pos, Dictionary<BlockPos, NetworkPart> parts, int startFace, BlockPos nextPos)
    {
        if (!parts.TryGetValue(pos, out var part))
            return false;

        var processedFaces = new bool[6];
        var queue = new Queue<BlockFacing>();
        var startFacing = FacingHelper.BlockFacingFromIndex(startFace);

        queue.Enqueue(startFacing);
        processedFaces[startFace] = true;

        // Проверка прямых соседей по граням
        foreach (var direction in FacingHelper.Directions(part.Connection))
        {
            var neighborPos = pos.AddCopy(direction);
            if (neighborPos == nextPos)
                return true;
        }

        // Проверка соседей по ребрам
        foreach (var direction in FacingHelper.Directions(part.Connection))
        {
            foreach (var face in FacingHelper.Faces(part.Connection))
            {
                if (face == direction || face == direction.Opposite)
                    continue;

                var neighborPos = pos.AddCopy(direction).AddCopy(face);
                if (neighborPos == nextPos && parts.ContainsKey(neighborPos))
                    return true;
            }
        }

        // BFS для поиска соединенных граней
        while (queue.Count > 0)
        {
            var currentFacing = queue.Dequeue();

            // Проверка всех направлений соединений
            foreach (var direction in FacingHelper.Directions(part.Connection))
            {
                if (!processedFaces[direction.Index] &&
                    (part.Connection & FacingHelper.FromFace(direction)) != 0)
                {
                    processedFaces[direction.Index] = true;
                    queue.Enqueue(direction);

                    // Прямая проверка позиции
                    var testPos = pos.AddCopy(direction);
                    if (testPos == nextPos)
                        return true;
                }
            }
        }

        return false;
    }

    */

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