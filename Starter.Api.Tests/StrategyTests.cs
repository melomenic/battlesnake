using Starter.Api.Logic;
using Starter.Api.Requests;

namespace Starter.Api.Tests;

public class StrategyTests
{
    [Fact]
    public void Corner_snake_moves_up_instead_of_into_a_wall_or_its_neck()
    {
        var you = Snake("you", 54, (0, 0), (1, 0), (2, 0));
        var other = Snake("other", 16, (5, 4), (5, 3), (6, 3), (6, 2));

        var move = SnakeStrategy.Decide(Request(you, new[] { you, other }, food: new[] { (5, 5), (9, 0), (2, 6) }, hazardDamage: 14, hazards: new[] { (3, 2) }));

        Assert.Equal("up", move.Move);
    }

    [Fact]
    public void Avoids_its_own_body()
    {
        var you = Snake("you", 80, (5, 5), (5, 4), (6, 4), (6, 5), (6, 6), (5, 6), (4, 6));

        var move = SnakeStrategy.Decide(Request(you, new[] { you }));

        Assert.Equal("left", move.Move);
    }

    [Fact]
    public void Avoids_another_snakes_body()
    {
        var you = Snake("you", 80, (5, 5), (5, 4), (5, 3));
        var other = Snake("other", 80, (5, 6), (4, 6), (3, 6));

        var move = SnakeStrategy.Decide(Request(you, new[] { you, other }));

        Assert.NotEqual("up", move.Move);
        Assert.Contains(move.Move, new[] { "left", "right" });
    }

    [Fact]
    public void Does_not_meet_a_longer_snake_head_on()
    {
        var you = Snake("you", 80, (5, 5), (5, 4), (5, 3));
        var longer = Snake("longer", 80, (5, 7), (6, 7), (7, 7), (8, 7), (9, 7), (9, 8), (9, 9), (8, 9), (7, 9), (6, 9));

        var move = SnakeStrategy.Decide(Request(you, new[] { you, longer }));

        Assert.NotEqual("up", move.Move);
    }

    [Fact]
    public void Prefers_the_open_board_over_a_one_cell_pocket()
    {
        var you = Snake("you", 90, (0, 1), (1, 1), (1, 0), (2, 0), (3, 0));

        var move = SnakeStrategy.Decide(Request(you, new[] { you }));

        Assert.Equal("up", move.Move);
    }

    [Fact]
    public void Does_not_enter_a_pocket_just_to_eat()
    {
        var you = Snake("you", 6, (0, 1), (1, 1), (1, 0), (2, 0), (3, 0));

        var move = SnakeStrategy.Decide(Request(you, new[] { you }, food: new[] { (0, 0) }));

        Assert.Equal("up", move.Move);
    }

    [Fact]
    public void Eats_when_both_moves_have_the_same_space()
    {
        var you = Snake("you", 100, (5, 5), (4, 5), (3, 5));

        var move = SnakeStrategy.Decide(Request(you, new[] { you }, food: new[] { (5, 8) }));

        Assert.Equal("up", move.Move);
        Assert.Equal("Moving toward food.", move.Shout);
    }

    [Fact]
    public void Seeks_food_before_health_runs_out()
    {
        var you = Snake("you", 8, (5, 5), (5, 4), (5, 3));

        var move = SnakeStrategy.Decide(Request(you, new[] { you }, food: new[] { (8, 5) }));

        Assert.Equal("right", move.Move);
    }

    [Fact]
    public void Eats_a_pellet_on_its_last_health_point_even_inside_a_hazard()
    {
        var you = Snake("you", 1, (5, 5), (5, 4), (5, 3));

        var move = SnakeStrategy.Decide(Request(
            you,
            new[] { you },
            food: new[] { (5, 6) },
            hazards: new[] { (5, 6) },
            hazardDamage: 50));

        Assert.Equal("up", move.Move);
        Assert.Equal("Eating to grow.", move.Shout);
    }

    [Fact]
    public void Avoids_a_hazard_that_would_drop_health_to_zero()
    {
        var you = Snake("you", 20, (5, 5), (5, 4), (5, 3));

        var move = SnakeStrategy.Decide(Request(you, new[] { you }, hazards: new[] { (5, 6) }, hazardDamage: 50));

        Assert.NotEqual("up", move.Move);
    }

    [Fact]
    public void Stacked_hazards_deal_damage_per_stack()
    {
        var you = Snake("you", 20, (5, 5), (5, 4), (5, 3));

        var move = SnakeStrategy.Decide(Request(
            you,
            new[] { you },
            hazards: new[] { (5, 6), (5, 6) },
            hazardDamage: 14));

        Assert.NotEqual("up", move.Move);
    }

    [Fact]
    public void Chases_its_own_tail_in_standard_and_not_in_constrictor()
    {
        var you = Snake("you", 50, (1, 1), (1, 0), (0, 0), (0, 1));
        var walls = Snake(
            "walls",
            50,
            (8, 8),
            (2, 0),
            (2, 2),
            (3, 1),
            (3, 0),
            (3, 2),
            (7, 8));

        var standard = SnakeStrategy.Decide(Request(you, new[] { you, walls }, hazards: new[] { (1, 2) }, hazardDamage: 100));
        var constrictor = SnakeStrategy.Decide(Request(
            you,
            new[] { you, walls },
            hazards: new[] { (1, 2) },
            hazardDamage: 100,
            ruleset: "constrictor",
            map: "constrictor"));

        Assert.Equal("left", standard.Move);
        Assert.Equal("up", constrictor.Move);
    }

    [Fact]
    public void Constrictor_does_not_starve_at_one_health()
    {
        var you = Snake("you", 1, (5, 5), (5, 4), (5, 3));

        var move = SnakeStrategy.Decide(Request(you, new[] { you }, ruleset: "constrictor", map: "constrictor"));

        Assert.Contains(move.Move, new[] { "up", "left", "right" });
        Assert.NotEqual("No safe square. Holding on.", move.Shout);
    }

    [Fact]
    public void Wrapped_board_cuts_across_the_edge_to_reach_food()
    {
        var you = Snake("you", 10, (0, 0), (0, 1), (0, 2));

        var move = SnakeStrategy.Decide(Request(you, new[] { you }, food: new[] { (10, 0) }, ruleset: "wrapped", map: "wrapped"));

        Assert.Equal("left", move.Move);
    }

    [Fact]
    public void Seals_a_shorter_snake_into_its_corner()
    {
        var you = Snake("you", 100, (2, 0), (2, 1), (2, 2), (2, 3), (2, 4));
        var enemy = Snake("enemy", 80, (0, 0), (0, 1), (0, 2));

        var move = SnakeStrategy.Decide(Request(you, new[] { you, enemy }));

        Assert.Equal("left", move.Move);
        Assert.Equal("Cutting off their space.", move.Shout);
    }

    [Fact]
    public void Squad_mate_body_is_safe_when_collisions_are_allowed()
    {
        var you = SquadSnake("you", (0, 0), (1, 0), (2, 0));
        var ally = SquadSnake("ally", (0, 1), (0, 2), (0, 3));

        var allowed = SnakeStrategy.Decide(Request(you, new[] { you, ally }, ruleset: "squad", allowBodyCollisions: true));
        var blocked = SnakeStrategy.Decide(Request(you, new[] { you, ally }, ruleset: "squad", allowBodyCollisions: false));

        Assert.Equal("up", allowed.Move);
        Assert.NotEqual("No safe square. Holding on.", allowed.Shout);
        Assert.Equal("No safe square. Holding on.", blocked.Shout);
    }

    private static GameStatusRequest Request(
        Snake you,
        IEnumerable<Snake> snakes,
        IEnumerable<(int X, int Y)>? food = null,
        IEnumerable<(int X, int Y)>? hazards = null,
        int hazardDamage = 0,
        string ruleset = "standard",
        string map = "standard",
        bool allowBodyCollisions = false)
    {
        return new GameStatusRequest
        {
            Turn = 1,
            You = you,
            Game = new Game
            {
                Id = "game",
                Map = map,
                Timeout = 500,
                Ruleset = new Ruleset
                {
                    Name = ruleset,
                    Version = "v1",
                    Settings = new RulesetSettings
                    {
                        HazardDamagePerTurn = hazardDamage,
                        MinimumFood = 1,
                        FoodSpawnChance = 15,
                        Squad = new RulesetSettings.SquadModeRulesetSettings
                        {
                            AllowBodyCollisions = allowBodyCollisions
                        }
                    }
                }
            },
            Board = new Board
            {
                Width = 11,
                Height = 11,
                Food = Cells(food),
                Hazards = Cells(hazards),
                Snakes = snakes
            }
        };
    }

    private static Snake Snake(string id, int health, params (int X, int Y)[] body)
    {
        return new Snake
        {
            Id = id,
            Name = id,
            Health = health,
            Length = body.Length,
            Head = new Coordinate(body[0].X, body[0].Y),
            Body = body.Select(part => new Coordinate(part.X, part.Y)).ToArray()
        };
    }

    private static Snake SquadSnake(string id, params (int X, int Y)[] body)
    {
        var snake = Snake(id, 80, body);
        snake.Squad = "1";
        return snake;
    }

    private static Coordinate[] Cells(IEnumerable<(int X, int Y)>? cells)
    {
        return (cells ?? Enumerable.Empty<(int X, int Y)>())
            .Select(cell => new Coordinate(cell.X, cell.Y))
            .ToArray();
    }
}
