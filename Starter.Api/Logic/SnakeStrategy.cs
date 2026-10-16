using Starter.Api.Requests;
using Starter.Api.Responses;

namespace Starter.Api.Logic;

/// <summary>
/// Chooses a move from the live board. Priorities, in order:
/// survive the turn, stay in a region large enough to turn around,
/// eat when that does not give up that space, then take space away from other snakes.
/// </summary>
public static class SnakeStrategy
{
    private static readonly (string Name, int Dx, int Dy)[] Directions =
    {
        ("up", 0, 1),
        ("down", 0, -1),
        ("right", 1, 0),
        ("left", -1, 0)
    };

    public static MoveResponse Decide(GameStatusRequest request)
    {
        var board = request.Board;
        var you = request.You;
        if (board.Width <= 0 || board.Height <= 0 || you.Head is null)
        {
            return new MoveResponse { Move = "up", Shout = "Waiting for a board." };
        }

        var context = BoardContext.From(request);
        var scored = new List<ScoredMove>(4);
        for (var i = 0; i < Directions.Length; i++)
        {
            var (name, dx, dy) = Directions[i];
            scored.Add(context.Score(name, dx, dy, i));
        }

        var best = scored
            .OrderByDescending(move => move.Survives)
            .ThenByDescending(move => move.EnoughSpace)
            .ThenByDescending(move => context.Hungry ? -move.FoodDistance : move.Space)
            .ThenByDescending(move => context.Hungry ? move.Space : -move.FoodDistance)
            .ThenByDescending(move => move.BoxScore)
            .ThenBy(move => move.HazardPenalty)
            .ThenByDescending(move => move.ContinuesStraight)
            .ThenBy(move => move.Index)
            .First();

        return new MoveResponse
        {
            Move = best.Move,
            Shout = context.ShoutFor(best, scored)
        };
    }

    private sealed class BoardContext
    {
        private readonly int _width;
        private readonly int _height;
        private readonly bool _wrapped;
        private readonly bool _constrictor;
        private readonly bool _squad;
        private readonly bool _allowBodyCollisions;
        private readonly int _hazardDamage;
        private readonly Point _head;
        private readonly string _youId;
        private readonly string _youSquad;
        private readonly int _youLength;
        private readonly int _youHealth;
        private readonly string? _straight;
        private readonly List<SnakeState> _snakes;
        private readonly HashSet<Point> _food;
        private readonly Dictionary<Point, int> _hazardStacks;
        private readonly int _foodDistanceNow;

        public bool Hungry { get; }

        private BoardContext(
            int width,
            int height,
            bool wrapped,
            bool constrictor,
            bool squad,
            bool allowBodyCollisions,
            int hazardDamage,
            Point head,
            string youId,
            string youSquad,
            int youLength,
            int youHealth,
            string? straight,
            List<SnakeState> snakes,
            HashSet<Point> food,
            Dictionary<Point, int> hazardStacks)
        {
            _width = width;
            _height = height;
            _wrapped = wrapped;
            _constrictor = constrictor;
            _squad = squad;
            _allowBodyCollisions = allowBodyCollisions;
            _hazardDamage = hazardDamage;
            _head = head;
            _youId = youId;
            _youSquad = youSquad;
            _youLength = youLength;
            _youHealth = youHealth;
            _straight = straight;
            _snakes = snakes;
            _food = food;
            _hazardStacks = hazardStacks;
            _foodDistanceNow = DistanceToFood(_head, Blocked(null, false), forUs: true);
            Hungry = !_constrictor
                && _foodDistanceNow != int.MaxValue
                && _youHealth <= _foodDistanceNow + 10;
        }

        public static BoardContext From(GameStatusRequest request)
        {
            var board = request.Board;
            var you = request.You;
            var map = request.Game?.Map ?? string.Empty;
            var rulesetName = request.Game?.Ruleset?.Name ?? string.Empty;
            var settings = request.Game?.Ruleset?.Settings;

            var wrapped = Contains(map, "wrapped") || Contains(rulesetName, "wrapped");
            var constrictor = Contains(map, "constrictor") || Contains(rulesetName, "constrictor");
            var squad = Contains(map, "squad") || Contains(rulesetName, "squad");

            var food = new HashSet<Point>();
            foreach (var cell in board.Food ?? Enumerable.Empty<Coordinate>())
            {
                food.Add(new Point(cell.X, cell.Y));
            }

            var hazards = new Dictionary<Point, int>();
            foreach (var cell in board.Hazards ?? Enumerable.Empty<Coordinate>())
            {
                if (cell.X < 0 || cell.Y < 0 || cell.X >= board.Width || cell.Y >= board.Height)
                {
                    continue;
                }

                var point = new Point(cell.X, cell.Y);
                hazards[point] = hazards.GetValueOrDefault(point) + 1;
            }

            var snakes = new List<SnakeState>();
            foreach (var snake in board.Snakes ?? Enumerable.Empty<Snake>())
            {
                snakes.Add(SnakeState.Create(snake));
            }

            if (snakes.All(snake => snake.Id != you.Id))
            {
                snakes.Add(SnakeState.Create(you));
            }

            var youState = snakes.First(snake => snake.Id == (you.Id ?? string.Empty));
            var head = youState.Body.Count > 0
                ? youState.Body[0]
                : new Point(you.Head.X, you.Head.Y);

            return new BoardContext(
                board.Width,
                board.Height,
                wrapped,
                constrictor,
                squad,
                settings?.Squad?.AllowBodyCollisions ?? false,
                settings?.HazardDamagePerTurn ?? 0,
                head,
                youState.Id,
                you.Squad ?? string.Empty,
                youState.Length,
                you.Health,
                StraightDirection(youState.Body, wrapped, board.Width, board.Height),
                snakes,
                food,
                hazards);
        }

        public ScoredMove Score(string move, int dx, int dy, int index)
        {
            var destination = Step(_head, dx, dy);
            var inBounds = InBounds(destination);
            var eats = inBounds && _food.Contains(destination);
            var blocked = Blocked(destination, eats);
            var bodyCrash = inBounds && HitsBody(destination, eats);
            var headLoss = inBounds && LosesHeadToHead(destination);
            var hazardPenalty = inBounds ? HazardPenalty(destination) : 0;
            var hazardKills = !_constrictor && !eats && hazardPenalty > 0 && _youHealth - 1 - hazardPenalty <= 0;
            var starves = !_constrictor && !eats && _youHealth <= 1;
            var survives = inBounds && !bodyCrash && !headLoss && !hazardKills && !starves;

            var space = inBounds ? Flood(destination, blocked, forUs: true) : 0;
            var lengthAfter = _youLength + (eats || _constrictor ? 1 : 0);
            var foodDistance = !inBounds
                ? int.MaxValue
                : eats
                    ? 0
                    : DistanceToFood(destination, blocked, forUs: true);

            return new ScoredMove(
                move,
                survives,
                survives && space >= lengthAfter,
                space,
                foodDistance,
                survives ? BoxScore(blocked) : 0,
                hazardPenalty,
                move == _straight,
                index);
        }

        public string ShoutFor(ScoredMove chosen, IReadOnlyList<ScoredMove> all)
        {
            if (!chosen.Survives)
            {
                return "No safe square. Holding on.";
            }

            if (chosen.FoodDistance == 0)
            {
                return "Eating to grow.";
            }

            var others = all.Where(move => move.Survives && move.Move != chosen.Move).ToList();
            var closerToFood = others.Count > 0
                && chosen.FoodDistance != int.MaxValue
                && others.All(move => chosen.FoodDistance < move.FoodDistance);
            if (closerToFood)
            {
                return Hungry
                    ? "Health is low. Moving toward food."
                    : "Moving toward food.";
            }

            if (Hungry && chosen.FoodDistance != int.MaxValue)
            {
                return "Health is low. Moving toward food.";
            }

            if (others.Count > 0 && others.All(move => chosen.BoxScore > move.BoxScore))
            {
                return "Cutting off their space.";
            }

            return "Staying where I can still turn.";
        }

        private HashSet<Point> Blocked(Point? ourDestination, bool weEat)
        {
            var blocked = new HashSet<Point>();
            foreach (var snake in _snakes)
            {
                var grows = snake.Id == _youId ? weEat || _constrictor : _constrictor || MightEat(snake);
                var tailLeaves = TailLeaves(snake.Body, grows);
                for (var i = 0; i < snake.Body.Count; i++)
                {
                    if (tailLeaves && i == snake.Body.Count - 1)
                    {
                        continue;
                    }

                    blocked.Add(snake.Body[i]);
                }
            }

            if (ourDestination is Point destination)
            {
                blocked.Add(destination);
            }

            return blocked;
        }

        private bool HitsBody(Point destination, bool weEat)
        {
            foreach (var snake in _snakes)
            {
                var grows = snake.Id == _youId ? weEat || _constrictor : _constrictor || MightEat(snake);
                var tailLeaves = TailLeaves(snake.Body, grows);
                for (var i = 0; i < snake.Body.Count; i++)
                {
                    if (snake.Body[i] != destination)
                    {
                        continue;
                    }

                    if (tailLeaves && i == snake.Body.Count - 1)
                    {
                        continue;
                    }

                    if (snake.Id != _youId && IsAlly(snake) && _allowBodyCollisions)
                    {
                        continue;
                    }

                    return true;
                }
            }

            return false;
        }

        private bool LosesHeadToHead(Point destination)
        {
            foreach (var snake in _snakes)
            {
                if (snake.Id == _youId || IsAlly(snake) || snake.Body.Count == 0)
                {
                    continue;
                }

                if (!IsOrthogonalNeighbor(snake.Body[0], destination))
                {
                    continue;
                }

                if (snake.Length >= _youLength)
                {
                    return true;
                }
            }

            return false;
        }

        private int BoxScore(HashSet<Point> blocked)
        {
            var score = 0;
            foreach (var snake in _snakes)
            {
                if (snake.Id == _youId || IsAlly(snake) || snake.Body.Count == 0)
                {
                    continue;
                }

                var space = Flood(snake.Body[0], blocked, forUs: false);
                score += (_width * _height) - space;
                if (space <= snake.Length)
                {
                    score += _width * _height;
                }
            }

            return score;
        }

        private bool MightEat(SnakeState snake)
        {
            if (snake.Body.Count == 0 || _food.Count == 0)
            {
                return false;
            }

            var head = snake.Body[0];
            foreach (var (_, dx, dy) in Directions)
            {
                var next = Step(head, dx, dy);
                if (InBounds(next) && _food.Contains(next))
                {
                    return true;
                }
            }

            return false;
        }

        private bool Walkable(Point cell, HashSet<Point> blocked, bool forUs)
        {
            if (!blocked.Contains(cell))
            {
                return true;
            }

            return forUs && PassableAlly(cell);
        }

        /// <summary>
        /// Squad mates can share a square when the ruleset allows it. Their bodies are not walls for us.
        /// </summary>
        private bool PassableAlly(Point cell)
        {
            if (!_allowBodyCollisions)
            {
                return false;
            }

            var foundAlly = false;
            foreach (var snake in _snakes)
            {
                if (!snake.Body.Contains(cell))
                {
                    continue;
                }

                if (snake.Id == _youId || !IsAlly(snake))
                {
                    return false;
                }

                foundAlly = true;
            }

            return foundAlly;
        }

        private bool IsAlly(SnakeState snake)
        {
            return _squad
                && !string.IsNullOrEmpty(_youSquad)
                && string.Equals(_youSquad, snake.Squad, StringComparison.Ordinal);
        }

        private int HazardPenalty(Point cell)
        {
            if (_hazardDamage <= 0 || !_hazardStacks.TryGetValue(cell, out var stacks))
            {
                return 0;
            }

            return stacks * _hazardDamage;
        }

        private int Flood(Point start, HashSet<Point> blocked, bool forUs)
        {
            var seen = new HashSet<Point> { start };
            var queue = new Queue<Point>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var (_, dx, dy) in Directions)
                {
                    var next = Step(current, dx, dy);
                    if (!InBounds(next) || !Walkable(next, blocked, forUs) || !seen.Add(next))
                    {
                        continue;
                    }

                    queue.Enqueue(next);
                }
            }

            return seen.Count;
        }

        private int DistanceToFood(Point start, HashSet<Point> blocked, bool forUs)
        {
            if (_food.Contains(start))
            {
                return 0;
            }

            var seen = new HashSet<Point> { start };
            var queue = new Queue<(Point Point, int Distance)>();
            queue.Enqueue((start, 0));

            while (queue.Count > 0)
            {
                var (current, distance) = queue.Dequeue();
                foreach (var (_, dx, dy) in Directions)
                {
                    var next = Step(current, dx, dy);
                    if (!InBounds(next) || !Walkable(next, blocked, forUs) || !seen.Add(next))
                    {
                        continue;
                    }

                    var nextDistance = distance + 1;
                    if (_food.Contains(next))
                    {
                        return nextDistance;
                    }

                    queue.Enqueue((next, nextDistance));
                }
            }

            return int.MaxValue;
        }

        private Point Step(Point origin, int dx, int dy)
        {
            var x = origin.X + dx;
            var y = origin.Y + dy;
            if (!_wrapped)
            {
                return new Point(x, y);
            }

            x = Mod(x, _width);
            y = Mod(y, _height);
            return new Point(x, y);
        }

        private bool InBounds(Point point)
        {
            return _wrapped || (point.X >= 0 && point.Y >= 0 && point.X < _width && point.Y < _height);
        }

        private bool IsOrthogonalNeighbor(Point a, Point b)
        {
            var dx = Delta(a.X, b.X, _width);
            var dy = Delta(a.Y, b.Y, _height);
            return dx + dy == 1;
        }

        private int Delta(int a, int b, int size)
        {
            var delta = Math.Abs(a - b);
            if (_wrapped && size > 0)
            {
                delta = Math.Min(delta, size - delta);
            }

            return delta;
        }

        private static bool TailLeaves(IReadOnlyList<Point> body, bool grows)
        {
            if (grows || body.Count == 0)
            {
                return false;
            }

            var tail = body[^1];
            var stacked = 0;
            for (var i = 0; i < body.Count; i++)
            {
                if (body[i] == tail)
                {
                    stacked++;
                }
            }

            return stacked == 1;
        }

        private static string? StraightDirection(IReadOnlyList<Point> body, bool wrapped, int width, int height)
        {
            if (body.Count < 2)
            {
                return null;
            }

            var dx = body[0].X - body[1].X;
            var dy = body[0].Y - body[1].Y;
            if (wrapped)
            {
                if (dx > 1)
                {
                    dx -= width;
                }
                else if (dx < -1)
                {
                    dx += width;
                }

                if (dy > 1)
                {
                    dy -= height;
                }
                else if (dy < -1)
                {
                    dy += height;
                }
            }

            return (dx, dy) switch
            {
                (0, 1) => "up",
                (0, -1) => "down",
                (-1, 0) => "left",
                (1, 0) => "right",
                _ => null
            };
        }

        private static bool Contains(string value, string part)
        {
            return value.Contains(part, StringComparison.OrdinalIgnoreCase);
        }

        private static int Mod(int value, int size)
        {
            if (size <= 0)
            {
                return value;
            }

            var remainder = value % size;
            return remainder < 0 ? remainder + size : remainder;
        }
    }

    private readonly record struct ScoredMove(
        string Move,
        bool Survives,
        bool EnoughSpace,
        int Space,
        int FoodDistance,
        int BoxScore,
        int HazardPenalty,
        bool ContinuesStraight,
        int Index);

    private readonly record struct Point(int X, int Y);

    private sealed class SnakeState
    {
        public string Id { get; init; } = string.Empty;
        public string Squad { get; init; } = string.Empty;
        public int Length { get; init; }
        public List<Point> Body { get; init; } = new();

        public static SnakeState Create(Snake snake)
        {
            var body = (snake.Body ?? Enumerable.Empty<Coordinate>())
                .Select(part => new Point(part.X, part.Y))
                .ToList();
            if (body.Count == 0 && snake.Head is not null)
            {
                body.Add(new Point(snake.Head.X, snake.Head.Y));
            }

            return new SnakeState
            {
                Id = snake.Id ?? string.Empty,
                Squad = snake.Squad ?? string.Empty,
                Length = Math.Max(snake.Length, body.Count),
                Body = body
            };
        }
    }
}
