using System;
using System.Collections.Generic;
using System.Linq;

namespace NsbRobots
{
    class Program
    {
        static void Main(string[] args)
        {

            var game = new Game(80, 40);

            game.AddRobot("A", new T1000());
            game.AddRobot("B", new T1000());
            game.AddRobot("C", new T1000());

            while (true)
            {
                Console.SetCursorPosition(0, 41);
                Console.ReadKey();
                game.Step();
            }
        }
    }

    class T1000 : IRobotControl
    {
        Bearing currentBearing;
        Random r = new Random();
        Coordinates lastEnemyPosition;

        public void Start(ICommands commands)
        {
            Turn(Bearing.South, commands);
            commands.Forward(2);
        }

        public void Collided(ICommands commands)
        {
            Turn(currentBearing.Opposite(), commands);
            commands.Forward(2);
        }

        public void Timeout(ICommands commands, object state)
        {
        }

        public void ObstacleAhead(ICommands commands)
        {
            if (r.Next(2) == 0)
            {
                Turn(currentBearing.Clockwise90(), commands);
            }
            else
            {
                Turn(currentBearing.CounterClockwise90(), commands);
            }
        }

        public void Hit(ICommands commands)
        {
        }

        public void EnemyDetected(ICommands commands, Coordinates enemyPosition)
        {
            if (lastEnemyPosition != null)
            {
                var moveVector = lastEnemyPosition.DistanceTo(enemyPosition);
                var projectedPosition = enemyPosition.Move(moveVector);
                commands.FireAt(projectedPosition);
            }
            else
            {
                commands.FireAt(enemyPosition);
            }
            lastEnemyPosition = enemyPosition;
        }
        void Turn(Bearing newBearing, ICommands commands)
        {
            currentBearing = newBearing;
            commands.Turn(currentBearing);
        }
    }
}

class Rectangle
{
    int top;
    int bottom;
    int left;
    int right;

    public Rectangle(Coordinates topLeft, Coordinates bottomRight)
    {
        top = topLeft.Y;
        left = topLeft.X;
        bottom = bottomRight.Y;
        right = bottomRight.X;
    }

    public Coordinates RandomCoordinates(Random r)
    {
        return new Coordinates(r.Next(left, right + 1), r.Next(top, bottom + 1));
    }

    public bool Contains(Coordinates coordinates)
    {
        return coordinates.X >= left
               && coordinates.X <= right
               && coordinates.Y >= top
               && coordinates.Y <= bottom;
    }
}

class Game
{
    List<Robot> robots = new List<Robot>();
    int turn;
    Rectangle arena;
    Random random = new Random();
    List<Coordinates> cleanupQueue = new List<Coordinates>();

    public Game(int width, int height)
    {
        arena = new Rectangle(new Coordinates(0, 0), new Coordinates(width - 1, height - 1));
    }

    public void AddRobot(string id, IRobotControl control)
    {
        Coordinates randomPosition;
        do
        {
            randomPosition = arena.RandomCoordinates(random);
        } while (robots.Any(x => x.Position == randomPosition));

        var r = new Robot
        {
            Id = id,
            Position = randomPosition,
            Bearing = Bearing.North,
            Velocity = 0,
            Control = control,
            HitPoints = 100,
        };
        robots.Add(r);
    }

    void Draw(Coordinates position, char symbol, ConsoleColor color)
    {
        if (!arena.Contains(position))
        {
            return;
        }
        Console.BackgroundColor = color;
        Console.SetCursorPosition(position.X, position.Y);
        Console.Write(symbol);
        Console.ResetColor();
        cleanupQueue.Add(position);
    }

    public void Step()
    {
        turn++;
        foreach (var c in cleanupQueue)
        {
            Console.ResetColor();
            Console.SetCursorPosition(c.X, c.Y);
            Console.Write(' ');
        }
        var ordered = robots.OrderByDescending(r => r.Velocity).ToArray();

        foreach (var robot in ordered)
        {
            robot.BeginTurn(turn);
        }

        Move(ordered);

        foreach (var robot in ordered)
        {
            Draw(robot.Position, robot.Id[0], ConsoleColor.Black);
        }

        //Timeouts
        foreach (var robot in ordered)
        {
            robot.ProcessTimeouts();
        }

        //Fire
        var targets = new List<Coordinates>();
        foreach (var robot in ordered)
        {
            if (robot.Target != null)
            {
                targets.Add(robot.Target);
                robot.Target = null;
            }
        }
        //Hit
        foreach (var target in targets)
        {
            var blastRadius = 2;

            Draw(target, '*', ConsoleColor.Red);

            var blastRange = new Rectangle(
                target.Move(blastRadius, Bearing.North).Move(blastRadius, Bearing.West),
                target.Move(blastRadius, Bearing.South).Move(blastRadius, Bearing.East));

            foreach (var robot in ordered)
            {
                if (blastRange.Contains(robot.Position))
                {
                    robot.Hit(10);
                    Draw(robot.Position, robot.Id[0], ConsoleColor.Red);
                }
            }
        }
        
        //Detect
        foreach (var robot in ordered)
        {
            var detectionRadius = 5;
            var detectionRange = new Rectangle(
                robot.Position.Move(detectionRadius, Bearing.North).Move(detectionRadius, Bearing.West),
                robot.Position.Move(detectionRadius, Bearing.South).Move(detectionRadius, Bearing.East));

            foreach (var candidate in ordered.Where(c => c != robot))
            {
                if (detectionRange.Contains(candidate.Position))
                {
                    robot.Control.EnemyDetected(robot, candidate.Position);
                }
            }
        }

        foreach (var robot in ordered)
        {
            if (robot.HitPoints <= 0)
            {
                robots.Remove(robot);
            }
        }
    }

    void Move(Robot[] ordered)
    {
        var topVelocity = ordered[0].Velocity;

        foreach (var robot in ordered)
        {
            var rangeFinderSpot = robot.Position.Move(8, robot.Bearing); //Probe distance in the moving direction
            if (!arena.Contains(rangeFinderSpot)
                && !robot.ObstacleWarningDelivered)
            {
                robot.DeliverObstacleAheadWarning = true;
            }
        }

        for (var step = topVelocity; step > 0; step--)
        {
            foreach (var robot in ordered)
            {
                if (robot.Velocity >= step)
                {
                    var newPosition = robot.Position.Move(1, robot.Bearing);
                    var collidedWith = robots.FirstOrDefault(r => r.Position == newPosition);
                    if (collidedWith != null) //collision with another robot
                    {
                        robot.Collided(10);
                        collidedWith.Collided(5);
                    }
                    else if (!arena.Contains(newPosition))
                    {
                        robot.Collided(5);
                    }
                    else
                    {
                        robot.Position = newPosition;
                    }
                }
            }
        }

        foreach (var robot in ordered)
        {
            robot.ProcessMoveFlags();
        }
    }
}

class Timeout
{
    public Timeout(int dueTime, object state)
    {
        DueTime = dueTime;
        State = state;
    }

    public int DueTime { get; }
    public object State { get; }

}

class Robot : ICommands
{
    int turn;

    public List<Timeout> timeouts = new List<Timeout>();

    public IRobotControl Control { get; set; }
    public int HitPoints { get; set; }
    public Coordinates Position { get; set; }
    public int Velocity { get; set; }
    public Bearing Bearing { get; set; }
    public bool ObstacleWarningDelivered { get; set; }
    public Coordinates Target { get; set; }

    public void BeginTurn(int turn)
    {
        this.turn = turn;
        if (turn == 1)
        {
            Control.Start(this);
        }
    }

    //Flags
    public bool DeliverCollidedWarning { get; set; }
    public bool DeliverObstacleAheadWarning { get; set; }
    public string Id { get; set; }

    public void ProcessTimeouts()
    {
        var dueTimeouts = timeouts.Where(t => t.DueTime == turn).ToArray();
        foreach (var dueTimeout in dueTimeouts)
        {
            timeouts.Remove(dueTimeout);
            Control.Timeout(this, dueTimeout.State);
        }
    }

    public void Collided(int damage)
    {
        DeliverCollidedWarning = true;
        HitPoints -= damage;
        Velocity = 0;
    }

    public void ProcessMoveFlags()
    {
        if (DeliverCollidedWarning)
        {
            Control.Collided(this);
        }
        else if (DeliverObstacleAheadWarning)
        {
            ObstacleWarningDelivered = true;
            Control.ObstacleAhead(this);
        }
        DeliverCollidedWarning = false;
        DeliverObstacleAheadWarning = false;
    }

    public void FireAt(Coordinates target)
    {
        Target = target;
    }

    public void Forward(int velocity)
    {
        if (velocity <= 5 && Velocity >= 0)
        {
            Velocity = velocity;
        }
    }

    public void Halt()
    {
        Velocity = 0;
    }

    public void Turn(Bearing newBearing)
    {
        ObstacleWarningDelivered = false;
        Bearing = newBearing;
    }

    public void RequestTimeout(int delay, object state)
    {
        timeouts.Add(new Timeout(turn + delay, state));
    }

    public void Hit(int damage)
    {
        HitPoints -= damage;
        Control.Hit(this);
    }
}

interface IRobotControl
{
    void Start(ICommands commands);
    void Collided(ICommands commands);
    void Timeout(ICommands commands, object state);
    void ObstacleAhead(ICommands commands);
    void Hit(ICommands commands);
    void EnemyDetected(ICommands commands, Coordinates enemyPosition);
}

interface ICommands
{
    void FireAt(Coordinates target);
    void Forward(int velocity);
    void Halt();
    void Turn(Bearing newBearing);
    void RequestTimeout(int delay, object state);
}

class Vector
{
    public Vector(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int X { get; }
    public int Y { get; }
}

class Coordinates
{
    public int X { get; }
    public int Y { get; }

    public Coordinates(int x, int y)
    {
        X = x;
        Y = y;
    }

    public Vector DistanceTo(Coordinates other)
    {
        return new Vector(other.X - X, other.Y - Y);
    }

    public Coordinates Move(Vector v)
    {
        return new Coordinates(X + v.X, Y + v.Y);
    }

    public Coordinates Move(int distance, Bearing bearing)
    {
        return bearing.Move(this, distance);
    }
}

class Bearing
{
    int x;
    int y;

    Bearing(int x, int y)
    {
        this.x = x;
        this.y = y;
    }


    public static Bearing North = new Bearing(0, -1);
    public static Bearing South = new Bearing(0, 1);
    public static Bearing West = new Bearing(-1, 0);
    public static Bearing East = new Bearing(1, 0);

    static Dictionary<Bearing, Bearing> opposite = new Dictionary<Bearing, Bearing>()
    {
        {East, West},
        {West, East},
        {North, South},
        {South, North}
    };

    static Dictionary<Bearing, Bearing> clockwise90 = new Dictionary<Bearing, Bearing>()
    {
        {East, South},
        {South, West},
        {West, North},
        {North, East}
    };

    static Dictionary<Bearing, Bearing> counterClockwise90 = new Dictionary<Bearing, Bearing>()
    {
        {East, North},
        {North, West},
        {West, South},
        {South, East}
    };


    public Bearing Clockwise90()
    {
        return clockwise90[this];
    }

    public Bearing CounterClockwise90()
    {
        return counterClockwise90[this];
    }

    public Bearing Opposite()
    {
        return opposite[this];
    }

    public Coordinates Move(Coordinates coordinates, int distance)
    {
        return new Coordinates(coordinates.X + distance * x, coordinates.Y + distance * y);
    }
}