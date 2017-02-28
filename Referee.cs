using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

class Referee : MultiReferee
{
    private static readonly int LEAGUE_LEVEL = 3;

    private static int MIN_FACTORY_COUNT = 7;
    private static int MAX_FACTORY_COUNT;
    private static int MIN_PRODUCTION_RATE = 0;
    private static int MAX_PRODUCTION_RATE = 3;
    private static int MIN_TOTAL_PRODUCTION_RATE = 4;
    private static int BOMBS_PER_PLAYER;
    private static int PLAYER_INIT_UNITS_MIN = 15;
    private static int PLAYER_INIT_UNITS_MAX = 30;
    private static int WIDTH = 16000;
    private static int HEIGHT = 6500;
    private static int EXTRA_SPACE_BETWEEN_FACTORIES = 300;
    private static int COST_INCREASE_PRODUCTION = 10;
    private static int DAMAGE_DURATION = 5;
    private static bool MOVE_RESTRICTION_ENABLED;
    private static bool INCREASE_ACTION_ENABLED;
    private static int FACTORY_RADIUS;

    private static readonly Regex PLAYER_INPUT_MOVE_PATTERN = new Regex(
        "MOVE (?<src>[0-9]{1,8})\\s+(?<dst>[0-9]{1,8})\\s+(?<units>([0-9]{1,8}))", RegexOptions.IgnoreCase);

    private static readonly Regex PLAYER_INPUT_WAIT_PATTERN = new Regex("WAIT", RegexOptions.IgnoreCase);

    private static readonly Regex PLAYER_INPUT_MSG_PATTERN =
        new Regex("MSG (?<message>.*)", RegexOptions.IgnoreCase);

    private static readonly Regex PLAYER_INPUT_BOMB_PATTERN =
        new Regex("BOMB (?<src>[0-9]{1,8})\\s+(?<dst>[0-9]{1,8})", RegexOptions.IgnoreCase);

    private static readonly Regex PLAYER_INPUT_INC_PATTERN =
        new Regex("INC (?<src>[0-9]{1,8})", RegexOptions.IgnoreCase);

    private static readonly Regex PLAYER_INPUT_ACTION_SEPARATOR_PATTERN =
        new Regex("\\s*;\\s*(?=WAIT|MOVE|BOMB|INC|MSG)", RegexOptions.IgnoreCase);

    static Referee()
    {
        switch (LEAGUE_LEVEL)
        {
            case 0: // Wood 3: only one move / turn, few factories
                MAX_FACTORY_COUNT = 9;
                MOVE_RESTRICTION_ENABLED = true;
                BOMBS_PER_PLAYER = 0;
                INCREASE_ACTION_ENABLED = false;
                break;
            case 1: // Wood 2: multiple actions / turn, more factories
                MAX_FACTORY_COUNT = 15;
                MOVE_RESTRICTION_ENABLED = false;
                BOMBS_PER_PLAYER = 0;
                INCREASE_ACTION_ENABLED = false;
                break;
            case 2: // Wood 1: add bombs
                MAX_FACTORY_COUNT = 15;
                MOVE_RESTRICTION_ENABLED = false;
                BOMBS_PER_PLAYER = 2;
                INCREASE_ACTION_ENABLED = false;
                break;
            default: // Other leagues: add increase action
                MAX_FACTORY_COUNT = 15;
                MOVE_RESTRICTION_ENABLED = false;
                BOMBS_PER_PLAYER = 2;
                INCREASE_ACTION_ENABLED = true;
                break;
        }
    }

    private Player[] players;
    private Factory[] factories;
    private List<Troop> troops;
    private List<Troop> newTroops;
    private List<Bomb> bombs;
    private List<Bomb> newBombs;
    private System.Random random;

// Properties
    private long seed;

    private int customFactoryCount;
    private int customInitialUnitCount;

    class EntityType
    {
        public static readonly EntityType FACTORY = new EntityType("FACTORY");
        public static readonly EntityType TROOP = new EntityType("TROOP");
        public static readonly EntityType BOMB = new EntityType("BOMB");

        private string name;

        private EntityType(string name)
        {
            this.name = name;
        }

        public override string ToString()
        {
            return name;
        }
    }

    private class Player
    {
        internal int id;
        internal List<MoveAction> lastMoveActions;
        internal List<BombAction> lastBombActions;
        internal List<IncAction> lastIncActions;
        internal string message;
        internal int score;
        private Factory[] factories;
        private List<Troop> troops;
        internal int remainingBombs;

        public Player(int id)
        {
            this.id = id;
            score = 0;
            remainingBombs = BOMBS_PER_PLAYER;
            lastMoveActions = new List<MoveAction>();
            lastBombActions = new List<BombAction>();
            lastIncActions = new List<IncAction>();
        }

        public void setDead()
        {
            // When a player is dead, it loses its factories and troops
            foreach (Factory factory in factories)
            {
                if (factory.owner == this)
                {
                    factory.owner = null;
                }
            }

            troops.RemoveAll(troop => troop.owner == this);

            score = 0;
        }

        public void setTroops(List<Troop> troops)
        {
            this.troops = troops;
        }

        public void setFactories(Factory[] factories)
        {
            this.factories = factories;
        }
    }

    abstract class Action
    {
    }

    class MoveAction : Action
    {
        internal Factory src;
        internal Factory dst;
        internal int units;

        public MoveAction(Factory src, Factory dst, int units)
        {
            this.src = src;
            this.dst = dst;
            this.units = units;
        }
    }

    class BombAction : Action
    {
        internal Factory src;
        internal Factory dst;

        public BombAction(Factory src, Factory dst)
        {
            this.src = src;
            this.dst = dst;
        }
    }

    class IncAction : Action
    {
        internal Factory src;

        public IncAction(Factory src)
        {
            this.src = src;
        }
    }

    abstract class Entity
    {
        private static int UNIQUE_ENTITY_ID;

        protected internal readonly int id;
        protected readonly EntityType type;

        public Entity(EntityType type)
        {
            id = UNIQUE_ENTITY_ID++;
            this.type = type;
        }

        public abstract string toPlayerString(int playerIdx);

        protected string toPlayerString(int arg1, int arg2, int arg3, int arg4, int arg5)
        {
            return id + " " + type + " " + arg1 + " " + arg2 + " " + arg3 + " " + arg4 + " " + arg5;
        }

        public virtual string toViewString()
        {
            return id + " " + type;
        }

        public virtual string toViewStringInit()
        {
            return id + " " + type;
        }
    }

    class Factory : Entity
    {
        internal Player owner;
        internal Vector2 position;
        internal int unitCount;
        internal int productionRate;
        internal int disabled;
        private Dictionary<int, int> distances;
        internal int[] unitsReadyToFight = {0, 0};

        public Factory(Player owner, int x, int y, int unitCount, int productionRate)
            : base(EntityType.FACTORY)
        {
            this.owner = owner;
            position = new Vector2(x, y);
            this.unitCount = unitCount;
            this.productionRate = productionRate;
        }

        public void computeDistances(Factory[] factories)
        {
            distances = new Dictionary<int, int>();
            foreach (Factory factory in factories)
            {
                if (this != factory)
                {
                    distances.Add(factory.id,
                        (int) (Math.Round(Vector2.Distance(position, factory.position) - getRadius() -
                                          factory.getRadius()) / 800f));
                }
            }
        }

        public int getDistanceTo(Factory factory)
        {
            return distances[factory.id];
        }

        public int getRadius()
        {
            return FACTORY_RADIUS;
        }

        public int getCurrentProductionRate()
        {
            return (disabled == 0) ? productionRate : 0;
        }

        public override string toPlayerString(int playerIdx)
        {
            int ownerShip = 0;
            if (owner != null)
            {
                ownerShip = (playerIdx == owner.id) ? 1 : -1;
            }
            return toPlayerString(ownerShip, unitCount, productionRate, disabled, 0);
        }

        public override string toViewStringInit()
        {
            return id + " " + productionRate + " " + position.x + " " + position.y + " " + getRadius();
        }

        public override string toViewString()
        {
            return (owner == null ? "-1" : Convert.ToString(owner.id)) + " " + unitCount + " " + productionRate +
                   " " + disabled;
        }
    }

    abstract class MovingEntity : Entity
    {
        protected internal Player owner;

        protected internal int remainingTurns;

        protected internal Factory source;
        protected internal Factory destination;

        public MovingEntity(EntityType type, Factory source, Factory destination)
            : base(type)
        {
            ;
            owner = source.owner;
            this.source = source;
            this.destination = destination;
            remainingTurns = source.getDistanceTo(destination);
        }

        public void move()
        {
            remainingTurns--;
        }

        public A findWithSameRouteInList<A>(List<A> list) where A : MovingEntity
        {
            foreach (A other in list)
            {
                if (other.source == source && other.destination == destination)
                {
                    return other;
                }
            }
            return null;
        }
    }

    class Bomb : MovingEntity
    {
        public Bomb(Factory source, Factory destination) : base(EntityType.BOMB, source, destination)
        {
        }

        public override string toPlayerString(int playerIdx)
        {
            if (owner.id == playerIdx)
            {
                return toPlayerString(1, source.id, destination.id, remainingTurns, 0);
            }
            return toPlayerString(-1, source.id, -1, -1, 0);
        }

        public override string toViewString()
        {
            return id + " " + (owner == null ? 0 : (owner.id)) + " " + source.id + " " + destination.id + " " +
                   remainingTurns;
        }

        public void explode()
        {
            int damage = Math.Min(destination.unitCount, Math.Max(10, destination.unitCount / 2));
            destination.unitCount -= damage;
            destination.disabled = DAMAGE_DURATION;
        }
    }

    class Troop : MovingEntity
    {
        internal int unitCount;

        public Troop(Factory source, Factory destination, int unitCount)
            : base(EntityType.TROOP, source, destination)
        {
            this.unitCount = unitCount;
        }

        public override string toPlayerString(int playerIdx)
        {
            int ownerShip = 0;
            if (owner != null)
            {
                ownerShip = (playerIdx == owner.id) ? 1 : -1;
            }
            return toPlayerString(ownerShip, source.id, destination.id, unitCount, remainingTurns);
        }

        public string toViewString()
        {
            return id + " " + (owner == null ? 0 : (owner.id)) + " " + source.id + " " + destination.id + " " +
                   unitCount +
                   " " + remainingTurns;
        }
    }

    protected override void initReferee(int playerCount, Properties prop)
    {
        long.TryParse(prop.getProperty("seed", Convert.ToString(new System.Random((int) Time.time))), out seed);

        string factoryCount = prop.getProperty<string>("factory_count");
        if (factoryCount != null)
        {
            int.TryParse(factoryCount, out customFactoryCount);
        }

        string initialUnitCount = prop.getProperty<string>("initial_unit_count");
        if
            (initialUnitCount != null)
        {
            int.TryParse(initialUnitCount, out customInitialUnitCount);
        }
        newTroops = new List<Troop>();
        newBombs = new List<Bomb>();
        random = new System.Random((int) seed);
        generatePlayers(playerCount);

        generateFactories();
        troops = new List<Troop>();
        bombs = new List<Bomb>();
        foreach (Player player in players)
        {
            player.setTroops(troops);
            player.setFactories(factories);
        }
    }

    void generatePlayers(int playerCount)
    {
        players = new Player [playerCount];
        for (int i = 0; i < playerCount; i++)
        {
            players[i] = new Player(i);
        }
    }

/**
 * Generate the factory objects
 */
    void generateFactories()
    {
        int factoryCount;
        if (customFactoryCount != null && customFactoryCount >= MIN_FACTORY_COUNT &&
            customFactoryCount <= MAX_FACTORY_COUNT)
        {
            factoryCount = customFactoryCount;
        }
        else
        {
            factoryCount = MIN_FACTORY_COUNT + random.Next(MAX_FACTORY_COUNT - MIN_FACTORY_COUNT + 1);
        }
        if (factoryCount % 2 == 0)
        {
// factoryCount must be odd
            factoryCount++;
        }
        FACTORY_RADIUS = factoryCount > 10 ? 600 : 700;
        int minSpaceBetweenFactories = 2 * (FACTORY_RADIUS + EXTRA_SPACE_BETWEEN_FACTORIES);
        factories = new Factory[factoryCount];
        int i = 0;

// Add one factory at the center of the map
        factories[i++] = new Factory(null, WIDTH / 2, HEIGHT / 2, 0, 0);
        while (i < factoryCount - 1)
        {
            int x = random.Next(WIDTH / 2 - 2 * FACTORY_RADIUS) + FACTORY_RADIUS +
                    EXTRA_SPACE_BETWEEN_FACTORIES;
            int y = random.Next(HEIGHT - 2 * FACTORY_RADIUS) + FACTORY_RADIUS +
                    EXTRA_SPACE_BETWEEN_FACTORIES;
            bool valid = true;
            for (int j = 0; j < i; j++)
            {
                Factory factory = factories[j];
                if (Vector2.Distance(factory.position, new Vector2(x, y)) < minSpaceBetweenFactories)
                {
                    valid = false;
                    break;
                }
            }
            if (valid)
            {
                int productionRate = MIN_PRODUCTION_RATE +
                                     random.Next(MAX_PRODUCTION_RATE - MIN_PRODUCTION_RATE + 1);
                if (i == 1)
                {
                    int unitCount;
                    if (customInitialUnitCount != null && customInitialUnitCount >= PLAYER_INIT_UNITS_MIN
                        && customInitialUnitCount <= PLAYER_INIT_UNITS_MAX)
                    {
                        unitCount = customInitialUnitCount;
                    }
                    else
                    {
                        unitCount = PLAYER_INIT_UNITS_MIN +
                                    random.Next(PLAYER_INIT_UNITS_MAX - PLAYER_INIT_UNITS_MIN + 1);
                    }
                    factories[i++] = new Factory(players[0], x, y, unitCount, productionRate);
                    factories[i++] =
                        new Factory(players[1], WIDTH - x, HEIGHT - y, unitCount, productionRate);
                }
                else
                {
                    int unitCount = random.Next(5 * productionRate + 1);
                    factories[i++] = new Factory(null, x, y, unitCount, productionRate);
                    factories[i++] = new Factory(null, WIDTH - x, HEIGHT - y, unitCount, productionRate);
                }
            }
        }
        int totalProductionRate = 0;
        foreach (Factory factory in factories)
        {
            factory.computeDistances(factories);
            totalProductionRate += factory.productionRate;
        }

// Make sure that the initial accumulated production rate for all the factories is at least MIN_TOTAL_PRODUCTION_RATE
        for (int j = 1; totalProductionRate < MIN_TOTAL_PRODUCTION_RATE && j < factories.Length; j++)
        {
            if (factories[j].productionRate < MAX_PRODUCTION_RATE)
            {
                factories[j].productionRate++;
                totalProductionRate++;
            }
        }
    }

    protected override Properties getConfiguration()
    {
        Properties prop = new Properties();
        prop.setProperty("seed", Convert.ToString(seed));
        if (customFactoryCount != null)
        {
            prop.setProperty("factory_count", Convert.ToString(customFactoryCount));
        }
        if (customInitialUnitCount != null)
        {
            prop.setProperty("initial_unit_count", Convert.ToString(customInitialUnitCount));
        }
        return prop;
    }

    protected override string[] getInitInputForPlayer(int playerIdx)
    {
        List<string> data = new List<string>();
        data.Add(Convert.ToString(factories.Length));

// Factory distances
        List<string> links = new List<string>();
        for (int i = 0; i < factories.Length; i++)
        {
            for (int j = i + 1; j < factories.Length; j++)
            {
                links.Add(factories[i].id + " " + factories[j].id + " " +
                          factories[i].getDistanceTo(factories[j]));
            }
        }
        data.Add(Convert.ToString(links.Count));
        data.AddRange(links);
        return data.ToArray();
    }

    protected override void prepare(int round)
    {
    }

    protected override string[] getInputForPlayer(int round, int playerIdx)
    {
        List<string> data = new List<string>();
        List<string> entities = new List<string>();
        foreach (Factory factory in factories)
        {
            entities.Add(factory.toPlayerString(playerIdx));
        }
        foreach (Troop troop in troops)
        {
            entities.Add(troop.toPlayerString(playerIdx));
        }
        foreach (Bomb bomb in bombs)
        {
            entities.Add(bomb.toPlayerString(playerIdx));
        }
        data.Add(Convert.ToString(entities.Count));
        data.AddRange(entities);
        return data.ToArray();
    }

    protected override int getExpectedOutputLineCountForPlayer(int playerIdx)
    {
        return 1;
    }

    protected override void handlePlayerOutput(int frame, int round, int playerIdx, string[] outputs)
    {
        Player player = players[playerIdx];
        player.lastBombActions.Clear();
        player.lastIncActions.Clear();
        player.lastMoveActions.Clear();
        player.message = null;
        try
        {
            foreach (string line in outputs)
            {
                foreach (string action in PLAYER_INPUT_ACTION_SEPARATOR_PATTERN.Split(line))
                {
                    var matchMove = PLAYER_INPUT_MOVE_PATTERN.Match(action);
                    var matchWait = PLAYER_INPUT_WAIT_PATTERN.Match(action);
                    var matchBomb = PLAYER_INPUT_BOMB_PATTERN.Match(action);
                    var matchInc = PLAYER_INPUT_INC_PATTERN.Match(action);
                    var matchMessage = PLAYER_INPUT_MSG_PATTERN.Match(action);
                    if (PLAYER_INPUT_MOVE_PATTERN.IsMatch(action))
                    {
                        if (MOVE_RESTRICTION_ENABLED && player.lastMoveActions.Count != 0)
                        {
// Silently ignore multiple moves
                            continue;
                        }
                        int src;
                        int.TryParse(matchMove.Groups["src"].Value, out src);
                        int dst;
                        int.TryParse(matchMove.Groups["dst"].Value, out dst);
                        int units;
                        int.TryParse(matchMove.Groups["units"].Value, out units);
                        if (src >= factories.Length)
                        {
                            throw new InvalidInputException("0 <= source < " + factories.Length + " " + src);
                        }
                        if (dst >= factories.Length)
                        {
                            throw new InvalidInputException("0 <= destination < " + factories.Length + " " + dst);
                        }
                        if (factories[src].owner != player)
                        {
                            throw new LostException("MoveFromNotControlledFactory" + " " + src);
                        }
                        if (src == dst)
                        {
                            throw new LostException("MoveSameSourceDestination" + " " + src);
                        }
                        player.lastMoveActions.Add(new MoveAction(factories[src], factories[dst], units));
                    }
                    else if (PLAYER_INPUT_BOMB_PATTERN.IsMatch(action))
                    {
                        int src;
                        int.TryParse(matchBomb.Groups["src"].Value, out src);
                        int dst;
                        int.TryParse(matchBomb.Groups["dst"].Value, out dst);
                        if (src >= factories.Length)
                        {
                            throw new InvalidInputException("0 <= source < " + factories.Length + " " + src);
                        }
                        if (dst >= factories.Length)
                        {
                            throw new InvalidInputException("0 <= destination < " + factories.Length + " " + dst);
                        }
                        if (factories[src].owner != player)
                        {
                            throw new LostException("BombFromNotControlledFactory " + src);
                        }
                        if (src == dst)
                        {
                            throw new LostException("BombSameSourceDestination " + src);
                        }
                        player.lastBombActions.Add(new BombAction(factories[src], factories[dst]));
                    }
                    else if (PLAYER_INPUT_INC_PATTERN.IsMatch(action))
                    {
                        if (!INCREASE_ACTION_ENABLED)
                        {
// Silently ignore increase actions
                            continue;
                        }
                        int src;
                        int.TryParse(matchInc.Groups["src"].Value, out src);
                        if (src >= factories.Length)
                        {
                            throw new InvalidInputException(
                                "0 <= source < " + factories.Length + " " + Convert.ToString(src));
                        }
                        if (factories[src].owner != player)
                        {
                            throw new LostException("IncFromNotControlledFactory " + src);
                        }
                        player.lastIncActions.Add(new IncAction(factories[src]));
                    }
                    else if (PLAYER_INPUT_WAIT_PATTERN.IsMatch(action))
                    {
// do nothing.
                    }
                    else if (PLAYER_INPUT_MSG_PATTERN.IsMatch(action))
                    {
                        string message = matchMessage.Groups["message"].Value.Trim();
                        if (message.Length > 100)
                        {
                            message = message.Substring(0, 100);
                        }
                        player.message = message;
                    }
                    else
                    {
                        throw new InvalidInputException("A valid action " + action);
                    }
                }
            }
        }
        catch (LostException le)
        {
            player.setDead();
            throw le;
        }
        catch (InvalidInputException iie)
        {
            player.setDead();
            throw iie;
        }
    }

    protected override void updateGame(int round)
    {
        newTroops.Clear();
        newBombs.Clear();

// ---
// Move troops and bombs
// ---
        foreach (Troop troop in troops)
        {
            troop.move();
        }
        foreach (Bomb bomb in bombs)
        {
            bomb.move();
        }

// ---
// Decrease disabled countdown
// ---
        foreach (Factory factory in factories)
        {
            if (factory.disabled > 0)
            {
                factory.disabled--;
            }
        }

// ---
// Execute orders
// ---
        foreach (Player player in players)
        {
// Send bombs
            foreach (BombAction bombAction in player.lastBombActions)
            {
                Bomb bomb = new Bomb(bombAction.src, bombAction.dst);
                if (player.remainingBombs > 0 && bomb.findWithSameRouteInList(newBombs) == null)
                {
                    newBombs.Add(bomb);
                    bombs.Add(bomb);
                    player.remainingBombs--;
                    addToolTip(player.id, translate("BombAction", player.id, bombAction.src.id, bombAction.dst.id));
                }
            }

// Send troops
            foreach (MoveAction moveAction in player.lastMoveActions)
            {
                int unitsToMove = Math.Min(moveAction.src.unitCount, moveAction.units);
                Troop troop = new Troop(moveAction.src, moveAction.dst, unitsToMove);
                if (unitsToMove > 0 && troop.findWithSameRouteInList(newBombs) == null)
                {
// Forbid sending units with the same source and destination as a bomb
                    moveAction.src.unitCount -= unitsToMove;
                    Troop other = troop.findWithSameRouteInList(newTroops);
                    if (other != null)
                    {
                        other.unitCount += unitsToMove;
                    }
                    else
                    {
                        troops.Add(troop);
                        newTroops.Add(troop);
                    }
                }
            }

// Increase
            foreach (IncAction incAction in player.lastIncActions)
            {
                if (incAction.src.unitCount >= COST_INCREASE_PRODUCTION && incAction.src.productionRate <
                    MAX_PRODUCTION_RATE)
                {
                    incAction.src.productionRate++;
                    incAction.src.unitCount -= COST_INCREASE_PRODUCTION;
                    addToolTip(player.id, translate("IncAction", player.id, incAction.src.id));
                }
            }
        }

// ---
// Create new units
// ---
        foreach (Factory factory in factories)
        {
            if (factory.owner != null)
            {
                factory.unitCount += factory.getCurrentProductionRate();
            }
        }

// ---
// Solve battles
// ---
        foreach (Factory factory in factories)
        {
            factory.unitsReadyToFight[0] = factory.unitsReadyToFight[1] = 0;
        }

        troops.RemoveAll(troop =>
        {
            if (troop.remainingTurns > 0) return false;
            troop.destination.unitsReadyToFight[troop.owner.id] += troop.unitCount;
            return true;
        });

        foreach (Factory factory in factories)
        {
// Units from both players fight first
            int units = Math.Min(factory.unitsReadyToFight[0], factory.unitsReadyToFight[1]);
            factory.unitsReadyToFight[0] -= units;
            factory.unitsReadyToFight[1] -= units;

// Remaining units fight on the factory
            foreach (Player player in players)
            {
                if (factory.owner == player)
                {
// Allied
                    factory.unitCount += factory.unitsReadyToFight[player.id];
                }
                else
                {
// Opponent
                    if (factory.unitsReadyToFight[player.id] > factory.unitCount)
                    {
                        factory.owner = player;
                        factory.unitCount = factory.unitsReadyToFight[player.id] - factory.unitCount;
                    }
                    else
                    {
                        factory.unitCount -= factory.unitsReadyToFight[player.id];
                    }
                }
            }
        }

// ---
// Solve bombs
// ---
        bombs.RemoveAll(bomb =>
        {
            if (bomb.remainingTurns > 0) return false;
            bomb.explode();
            return true;
        });
// ---
// Update score
// ---
        foreach (Player player in players)
        {
            player.score = 0;
        }
        foreach (Factory factory in factories)
        {
            if (factory.owner != null)
            {
                factory.owner.score += factory.unitCount;
            }
        }
        foreach (Troop troop in troops)
        {
            if (troop.owner != null)
            {
                troop.owner.score += troop.unitCount;
            }
        }

// ---
// Check end conditions
// ---
        foreach (Player player in players)
        {
            if (player.score == 0)
            {
                int production = 0;
                foreach (Factory factory in factories)
                {
                    if (factory.owner == player)
                    {
                        production += factory.productionRate;
                    }
                }
                if (production == 0)
                {
                    throw new GameOverException("endReached");
                }
            }
        }
    }

    protected override void populateMessages(Properties p)
    {
        p.put("endReached", "End reached");
    }

    protected override string[] getInitDataForView()
    {
        List<string> data = new List<string>();
        data.Add(WIDTH + " " + HEIGHT + " " + factories.Length + " " + BOMBS_PER_PLAYER);
        foreach (Factory factory in factories)
        {
            data.Add(factory.toViewStringInit());
        }
        data.Insert(0, Convert.ToString(data.Count + 1));
        return data.ToArray();
    }

    protected override string[] getFrameDataForView(int round, int frame, bool keyFrame)
    {
        List<string> data = new List<string>();
// Pass the scores and messages
        for (int playerIdx = 0; playerIdx < players.Length; ++playerIdx)
        {
            string playerInfo = Convert.ToString(getScore(playerIdx)) + " " + players[playerIdx].remainingBombs;
            if (players[playerIdx].message != null)
            {
                playerInfo += " " + players[playerIdx].message;
            }
            data.Add(playerInfo);
        }

// Pass the troops
        List<string> troopData = new List<string>();
        foreach (Troop troop in newTroops)
        {
            troopData.Add(troop.toViewString());
        }
        data.Add(Convert.ToString(troopData.Count));
        data.AddRange(troopData);

// Pass the bombs
        List<string> bombData = new List<string>();
        foreach (Bomb bomb in newBombs)
        {
            bombData.Add(bomb.toViewString());
        }
        data.Add(Convert.ToString(bombData.Count));
        data.AddRange(bombData);

// Pass the factories
        foreach (Factory factory in factories)
        {
            data.Add(factory.toViewString());
        }
        return data.ToArray();
    }

    protected override string getGameName()
    {
        return "GhostInTheCell";
    }

    protected override string getHeadlineAtGameStartForConsole()
    {
        return null;
    }

    protected override int getMinimumPlayerCount()
    {
        return 2;
    }

    protected override bool showTooltips()
    {
        return true;
    }

    protected override string[] getPlayerActions(int playerIdx, int round)
    {
        return new string[0];
    }

    protected override bool isPlayerDead(int playerIdx)
    {
        return false;
    }

    protected override string getDeathReason(int playerIdx)
    {
        return "$" + playerIdx + ": Eliminated!";
    }

    protected override int getScore(int playerIdx)
    {
        return players[playerIdx].score;
    }

    protected override string[] getGameSummary(int round)
    {
        return new string[0];
    }

    protected override void setPlayerTimeout(int frame, int round, int playerIdx)
    {
        players[playerIdx].setDead();
    }

    protected override int getMaxRoundCount(int playerCount)
    {
        return 200;
    }

    protected override int getMillisTimeForRound()
    {
        return 50;
    }

//    public static void main(string...args)
//    {
//        new Referee(System.in, System.out, System.err);
//    }
//
//    public Referee(InputStream is, PrintStream out, PrintStream err)
//    {
//        base( is, out,
//        err)
//        ;
//    }
}

public abstract class MultiReferee
{
    protected abstract Properties getConfiguration();

    protected abstract string[] getInitInputForPlayer(int playerIdx);

    protected abstract void prepare(int round);

    protected abstract string[] getInputForPlayer(int round, int playerIdx);

    protected abstract int getExpectedOutputLineCountForPlayer(int playerIdx);

    protected abstract void handlePlayerOutput(int frame, int round, int playerIdx, string[] outputs);

    protected abstract void updateGame(int round);

    protected abstract void populateMessages(Properties p);

    protected abstract string getGameName();

    protected abstract string getHeadlineAtGameStartForConsole();

    protected abstract int getMinimumPlayerCount();

    protected abstract bool showTooltips();

    protected abstract string[] getPlayerActions(int playerIdx, int round);

    protected abstract bool isPlayerDead(int playerIdx);

    protected abstract string getDeathReason(int playerIdx);

    protected abstract int getScore(int playerIdx);

    protected abstract string[] getGameSummary(int round);

    protected abstract void setPlayerTimeout(int frame, int round, int playerIdx);

    protected abstract int getMaxRoundCount(int playerCount);

    protected abstract int getMillisTimeForRound();

    protected abstract void initReferee(int playerCount, Properties prop);

    protected abstract string[] getFrameDataForView(int round, int frame, bool keyFrame);

    protected abstract string[] getInitDataForView();

    protected void addToolTip(int id, string message)
    {
        Debug.Log(id + " " + message);
    }

    internal string translate(string name, int playerId, int sourceId, int targetId)
    {
        return "";
    }

    internal string translate(string name, int sourceId, int targetId)
    {
        return "";
    }
}

public class Properties
{
    internal void setProperty(string name, object value)
    {
    }

    internal void put(string name, object value)
    {
    }

    internal T getProperty<T>(string name, T value = default(T))
    {
        return default(T);
    }
}

[Serializable]
public class InvalidInputException : Exception
{
    public InvalidInputException()
    {
    }

    public InvalidInputException(string message) : base(message)
    {
    }

    public InvalidInputException(string message, Exception inner) : base(message, inner)
    {
    }

    protected InvalidInputException(
        SerializationInfo info,
        StreamingContext context) : base(info, context)
    {
    }
}

[Serializable]
public class LostException : Exception
{
    public LostException()
    {
    }

    public LostException(string message) : base(message)
    {
    }

    public LostException(string message, Exception inner) : base(message, inner)
    {
    }

    protected LostException(
        SerializationInfo info,
        StreamingContext context) : base(info, context)
    {
    }
}

[Serializable]
public class GameOverException : Exception
{
    public GameOverException()
    {
    }

    public GameOverException(string message) : base(message)
    {
    }

    public GameOverException(string message, Exception inner) : base(message, inner)
    {
    }

    protected GameOverException(
        SerializationInfo info,
        StreamingContext context) : base(info, context)
    {
    }
}