﻿using System;
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
    public (List<BlockPos>, List<int>, List<bool[]>) FindShortestPath(BlockPos start, BlockPos end, Network network, Dictionary<BlockPos, NetworkPart> parts)
    {

        //проверяем наличие начальной и конечной точки в этой цепи
        var networkPositions = network.PartPositions;
        if (!networkPositions.Contains(start) || !networkPositions.Contains(end))
            return (null!, null!, null!);


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

        //хранит номер задействованной грани соседа (для вывода наружу)
        var facingFromList = new List<int>();

        //хранит для каждого кусочка цепи посещенные грани в данный момент
        var nowProcessedFaces = new Dictionary<(BlockPos, int), bool[]>();
        nowProcessedFaces[(start, startBlockFacing[0])] = new bool[6] { false, false, false, false, false, false };
        nowProcessedFaces[(start, startBlockFacing[0])][startBlockFacing[0]] = true;

        //хранит для каждого кусочка цепи посещенные грани в данный момент (для вывода наружу)
        var nowProcessedFacesList = new List<bool[]>();


        //хранит для каждого кусочка цепи все посещенные грани
        var processedFaces = new Dictionary<BlockPos, bool[]>();
        foreach (var index in networkPositions)
        {
            processedFaces[index] = new bool[6] { false, false, false, false, false, false };
        }

        bool first = true;                      //маркер для первого прохода

        while (queue.Count > 0)                 //пока очередь не опустеет
        {
            var (currentPos, currentFace) = queue.Dequeue();

            if (currentPos.Equals(end))            //достигли конца и прекращаем просчет
                break;


            //собственно сам поиск соседа данной точке
            var (buf1, buf2, buf3, buf4) = GetNeighbors(currentPos, network, parts, first, processedFaces[currentPos], facingFrom[(currentPos, currentFace)]);



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
            return (null!, null!, null!);

        var (path, faces) = ReconstructPath(start, end, endBlockFacing[0], cameFrom);    //реконструкция маршрута

        for (int i = 0; i < path.Count; i++)                                //подготавливаем дополнительные данные
        {
            facingFromList.Add(facingFrom[(path[i], faces[i])]);


            //if (i > 0)
            nowProcessedFacesList.Add(nowProcessedFaces[(path[i], faces[i])]);

        }

        //nowProcessedFacesList.Add(new bool[6] { true, true, true, true, true, true });



        return (path, facingFromList, nowProcessedFacesList);
    }



    /// <summary>
    /// Вычисляет позиции соседей от текущего значения
    /// </summary>
    /// <param name="pos"></param>
    /// <param name="network"></param>
    /// <returns></returns>
    private (List<BlockPos>, List<int>, bool[], bool[]) GetNeighbors(BlockPos pos, Network network, Dictionary<BlockPos, NetworkPart> parts, bool first, bool[] processFaces, int startFace)
    {
        var networkPositions = network.PartPositions;               //позиции в этой сети

        List<BlockPos> Neighbors = new List<BlockPos>();  //координата соседа
        List<int> NeighborsFace = new List<int>();        //грань соседа с которым мы взаимодействовать будем

        bool[] NowProcessed = new bool[6] { false, false, false, false, false, false };       //задействованныхе грани в этой точке                             

        var part = parts[pos];                     //текущий элемент
        var Connections = part.Connection;         //соединения этого элемента


        //нужно отобрать только те Connection, которые относятся к этой цепи
        bool[] faces = new bool[6] { false, false, false, false, false, false };

        int i = 0;
        foreach (var net in part.Networks)
        {
            if (net == network)
            {
                faces[i] = true;
            }
            i++;

        }


        //оставляем только соединеия этой цепи, сразу выкидывая обработанные
        Facing hereConnections = Facing.None;
        Facing[] faceMasks = new Facing[]
        {
            Facing.NorthAll,  // 0: North
            Facing.EastAll,   // 1: East
            Facing.SouthAll,  // 2: South
            Facing.WestAll,   // 3: West
            Facing.UpAll,     // 4: Up
            Facing.DownAll    // 5: Down
        };


        for (i = 0; i < faces.Length; i++)
        {
            if (faces[i] && !processFaces[i])  //фильтруем по соединениям этой цепи и уже пройденным граням
            {
                hereConnections |= part.Connection & faceMasks[i];
            }
        }



        //теперь нужно выяснить с какой гранью мы работаем и соединены ли грани одной цепи
        int startFaceIndex = startFace;

        // Инициализация BFS
        var queue = new Queue<int>();
        queue.Enqueue(startFaceIndex);
        bool[] processFacesBuf = new bool[6];
        processFaces.CopyTo(processFacesBuf, 0);
        processFacesBuf[startFaceIndex] = true;


        BlockFacing startBlockFacing = FacingHelper.BlockFacingFromIndex(startFaceIndex);


        // Поиск всех связанных граней
        while (queue.Count > 0)
        {
            int currentFaceIndex = queue.Dequeue();
            BlockFacing currentFace = FacingHelper.BlockFacingFromIndex(currentFaceIndex);

            // Получаем соединения текущей грани
            Facing currentFaceMask = FacingHelper.FromFace(currentFace);
            Facing connections = hereConnections & currentFaceMask;

            // Перебираем все направления соединений
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
        Facing validConnectionsMask = processFacesBuf
            .Select((processed, index) => processed ? FacingHelper.FromFace(FacingHelper.BlockFacingFromIndex(index)) : Facing.None)
            .Aggregate(Facing.None, (mask, curr) => mask | curr);

        hereConnections &= validConnectionsMask;




        //ищем соседей по граням
        foreach (var direction in FacingHelper.Directions(hereConnections))
        {
            var directionFilter = FacingHelper.FromDirection(direction);
            var neighborPosition = part.Position.AddCopy(direction);

            if (parts.TryGetValue(neighborPosition, out var neighborPart))         //проверяет, если в той стороне сосед
            {
                foreach (var face in FacingHelper.Faces(hereConnections & directionFilter))
                {
                    if ((neighborPart.Connection & FacingHelper.From(face, direction.Opposite)) != 0)
                    {

                        if (networkPositions.Contains(neighborPosition))
                        {
                            Neighbors.Add(neighborPosition);                //координата соседа
                            NeighborsFace.Add(face.Index);                  //номер грани соседа
                            NowProcessed[face.Index] = true;                //обработанная грань сейчас
                            processFaces[face.Index] = true;                //все обработанные грани
                        }
                    }

                    if ((neighborPart.Connection & FacingHelper.From(direction.Opposite, face)) != 0)
                    {
                        if (networkPositions.Contains(neighborPosition))
                        {
                            Neighbors.Add(neighborPosition);                //координата соседа
                            NeighborsFace.Add(direction.Opposite.Index);    //номер грани соседа
                            NowProcessed[face.Index] = true;                //обработанная грань сейчас
                            processFaces[face.Index] = true;                //все обработанные грани
                        }
                    }
                }
            }
        }

        //ищем соседей по ребрам
        foreach (var direction in FacingHelper.Directions(hereConnections))
        {
            var directionFilter = FacingHelper.FromDirection(direction);

            foreach (var face in FacingHelper.Faces(hereConnections & directionFilter))
            {
                var neighborPosition = part.Position.AddCopy(direction).AddCopy(face);

                if (parts.TryGetValue(neighborPosition, out var neighborPart))
                {
                    if ((neighborPart.Connection & FacingHelper.From(direction.Opposite, face.Opposite)) != 0)
                    {
                        if (networkPositions.Contains(neighborPosition))
                        {
                            Neighbors.Add(neighborPosition);                //координата соседа
                            NeighborsFace.Add(direction.Opposite.Index);    //номер грани соседа
                            NowProcessed[face.Index] = true;                //обработанная грань сейчас
                            processFaces[face.Index] = true;                //все обработанные грани
                        }
                    }

                    if ((neighborPart.Connection & FacingHelper.From(face.Opposite, direction.Opposite)) != 0)
                    {
                        if (networkPositions.Contains(neighborPosition))
                        {
                            Neighbors.Add(neighborPosition);                //координата соседа
                            NeighborsFace.Add(face.Opposite.Index);         //номер грани соседа
                            NowProcessed[face.Index] = true;                //обработанная грань сейчас
                            processFaces[face.Index] = true;                //все обработанные грани
                        }
                    }
                }
            }
        }



        //возвращаем координаты соседей, котактирующую грань соседа, задействованные грани в этой точке сейчас, все обработанные грани этой цепи в этой точке 
        return (Neighbors, NeighborsFace, NowProcessed, processFaces);
    }



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


    /// <summary>
    /// Реконструирует маршрут
    /// </summary>
    /// <param name="start"></param>
    /// <param name="end"></param>
    /// <param name="endFacing"></param>
    /// <param name="cameFrom"></param>
    /// <returns></returns>
    private (List<BlockPos>, List<int>) ReconstructPath(BlockPos start, BlockPos end, int endFacing, Dictionary<(BlockPos, int), (BlockPos, int)> cameFrom)
    {
        var path = new List<BlockPos>();
        var faces = new List<int>();
        var current = (end, endFacing);

        while (current.end != null)
        {
            path.Add(current.end);
            faces.Add(current.endFacing);
            current = cameFrom[current];
        }

        path.Reverse();
        faces.Reverse();
        return path[0].Equals(start) ? (path, faces) : (null!, null!);
    }
}