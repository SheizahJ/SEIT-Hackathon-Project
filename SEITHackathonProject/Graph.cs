using System;
using System.Collections.Generic;

public class Graph
{
    private Dictionary<string, List<Edge>> adjList;

    public Graph()
    {
        adjList = new Dictionary<string, List<Edge>>();
    }

    public void AddEdge(string from, string to, double weight)
    {
        if (!adjList.ContainsKey(from)) adjList[from] = new List<Edge>();
        adjList[from].Add(new Edge(to, weight));
    }

    public List<string> Dijkstra(string start, string end)
    {
        var distances = new Dictionary<string, double>();
        var previous = new Dictionary<string, string>();
        var priorityQueue = new SortedList<double, List<string>>();
        var path = new List<string>();

        foreach (var node in adjList)
        {
            distances[node.Key] = double.MaxValue;
            previous[node.Key] = null;
        }

        distances[start] = 0;
        priorityQueue.Add(0, new List<string> { start });

        while (priorityQueue.Count > 0)
        {
            var currentDist = priorityQueue.Keys[0];
            var currentNode = priorityQueue[currentDist][0];
            priorityQueue[currentDist].RemoveAt(0);

            if (priorityQueue[currentDist].Count == 0)
                priorityQueue.RemoveAt(0);

            foreach (var edge in adjList[currentNode])
            {
                double newDist = currentDist + edge.Weight;

                if (newDist < distances[edge.To])
                {
                    distances[edge.To] = newDist;
                    previous[edge.To] = currentNode;

                    if (!priorityQueue.ContainsKey(newDist))
                        priorityQueue.Add(newDist, new List<string> { edge.To });
                    else
                        priorityQueue[newDist].Add(edge.To);
                }
            }
        }

        // Reconstruct the shortest path
        var currentNodeForPath = end;
        while (currentNodeForPath != null)
        {
            path.Insert(0, currentNodeForPath);
            currentNodeForPath = previous[currentNodeForPath];
        }

        return path;
    }
}

public class Edge
{
    public string To { get; }
    public double Weight { get; }

    public Edge(string to, double weight)
    {
        To = to;
        Weight = weight;
    }
}
