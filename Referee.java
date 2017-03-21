import java.awt.Point;
import java.io.IOException;
import java.io.InputStream;
import java.io.PrintStream;
import java.io.PrintWriter;
import java.io.StringReader;
import java.io.StringWriter;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashSet;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.LinkedList;
import java.util.List;
import java.util.Map;
import java.util.Map.Entry;
import java.util.Properties;
import java.util.Random;
import java.util.Scanner;
import java.util.Set;
import java.util.regex.Matcher;
import java.util.regex.Pattern;


class Referee extends MultiReferee {

    private static final int LEAGUE_LEVEL = 3;

    private static final int MIN_FACTORY_COUNT = 7;
    private static final int MAX_FACTORY_COUNT;
    private static final int MIN_PRODUCTION_RATE = 0;
    private static final int MAX_PRODUCTION_RATE = 3;
    private static final int MIN_TOTAL_PRODUCTION_RATE = 4;
    private static final int BOMBS_PER_PLAYER;
    private static final int PLAYER_INIT_UNITS_MIN = 15;
    private static final int PLAYER_INIT_UNITS_MAX = 30;
    private static final int WIDTH = 16000;
    private static final int HEIGHT = 6500;
    private static final int EXTRA_SPACE_BETWEEN_FACTORIES = 300;
    private static final int COST_INCREASE_PRODUCTION = 10;
    private static final int DAMAGE_DURATION = 5;
    private static final boolean MOVE_RESTRICTION_ENABLED;
    private static final boolean INCREASE_ACTION_ENABLED;
    private static int FACTORY_RADIUS;

    private static final Pattern PLAYER_INPUT_MOVE_PATTERN = Pattern
            .compile("MOVE (?<src>[0-9]{1,8})\\s+(?<dst>[0-9]{1,8})\\s+(?<units>([0-9]{1,8}))", Pattern.CASE_INSENSITIVE);
    private static final Pattern PLAYER_INPUT_WAIT_PATTERN = Pattern.compile("WAIT", Pattern.CASE_INSENSITIVE);
    private static final Pattern PLAYER_INPUT_MSG_PATTERN = Pattern.compile("MSG (?<message>.*)", Pattern.CASE_INSENSITIVE);
    private static final Pattern PLAYER_INPUT_BOMB_PATTERN = Pattern.compile("BOMB (?<src>[0-9]{1,8})\\s+(?<dst>[0-9]{1,8})",
            Pattern.CASE_INSENSITIVE);
    private static final Pattern PLAYER_INPUT_INC_PATTERN = Pattern.compile("INC (?<src>[0-9]{1,8})", Pattern.CASE_INSENSITIVE);
    private static final Pattern PLAYER_INPUT_ACTION_SEPARATOR_PATTERN = Pattern.compile("\\s*;\\s*(?=WAIT|MOVE|BOMB|INC|MSG)",
            Pattern.CASE_INSENSITIVE);

    static {
        switch (LEAGUE_LEVEL) {
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
        }
    }

    private Player[] players;
    private Factory[] factories;
    private List<Troop> troops;
    private List<Troop> newTroops;
    private List<Bomb> bombs;
    private List<Bomb> newBombs;
    private Random random;

    // Properties
    private long seed;
    private Integer customFactoryCount;
    private Integer customInitialUnitCount;

    private static enum EntityType {
        FACTORY("FACTORY"), TROOP("TROOP"), BOMB("BOMB");

        private String name;

        private EntityType(final String name) {
            this.name = name;
        }

        @Override
        public String toString() {
            return this.name;
        }
    }

    private static class Player {
        private int id;
        private List<MoveAction> lastMoveActions;
        private List<BombAction> lastBombActions;
        private List<IncAction> lastIncActions;
        private String message;
        private int score;
        private Factory[] factories;
        private List<Troop> troops;
        private int remainingBombs;

        public Player(int id) {
            this.id = id;
            this.score = 0;
            this.remainingBombs = BOMBS_PER_PLAYER;
            this.lastMoveActions = new ArrayList<>();
            this.lastBombActions = new ArrayList<>();
            this.lastIncActions = new ArrayList<>();
        }

        public void setDead() {
            // When a player is dead, it loses its factories and troops
            for (Factory factory : factories) {
                if (factory.owner == this) {
                    factory.owner = null;
                }
            }
            for (Iterator<Troop> it = troops.iterator(); it.hasNext();) {
                Troop troop = it.next();
                if (troop.owner == this) {
                    it.remove();
                }
            }
            this.score = 0;
        }

        public void setTroops(List<Troop> troops) {
            this.troops = troops;
        }

        public void setFactories(Factory[] factories) {
            this.factories = factories;
        }
    }

    private static abstract class Action {
    }

    private static class MoveAction extends Action {
        private Factory src;
        private Factory dst;
        private int units;

        public MoveAction(Factory src, Factory dst, int units) {
            this.src = src;
            this.dst = dst;
            this.units = units;
        }
    }

    private static class BombAction extends Action {
        private Factory src;
        private Factory dst;

        public BombAction(Factory src, Factory dst) {
            this.src = src;
            this.dst = dst;
        }
    }

    private static class IncAction extends Action {
        private Factory src;

        public IncAction(Factory src) {
            this.src = src;
        }
    }

    private static abstract class Entity {
        private static int UNIQUE_ENTITY_ID = 0;

        protected final int id;
        protected final EntityType type;

        public Entity(EntityType type) {
            this.id = UNIQUE_ENTITY_ID++;
            this.type = type;
        }

        public abstract String toPlayerString(int playerIdx);

        protected String toPlayerString(int arg1, int arg2, int arg3, int arg4, int arg5) {
            return id + " " + type + " " + arg1 + " " + arg2 + " " + arg3 + " " + arg4 + " " + arg5;
        }
    }

    private static class Factory extends Entity {
        private Player owner;
        private Point position;
        private int unitCount;
        private int productionRate;
        private int disabled;
        private Map<Integer, Integer> distances;

        private int[] unitsReadyToFight = { 0, 0 };

        public Factory(Player owner, int x, int y, int unitCount, int productionRate) {
            super(EntityType.FACTORY);
            this.owner = owner;
            this.position = new Point(x, y);
            this.unitCount = unitCount;
            this.productionRate = productionRate;
        }

        public void computeDistances(Factory[] factories) {
            distances = new LinkedHashMap<>();
            for (Factory factory : factories) {
                if (this != factory) {
                    distances.put(factory.id, (int) Math.round((position.distance(factory.position) - getRadius() - factory.getRadius()) / 800.));
                }
            }
        }

        public int getDistanceTo(Factory factory) {
            return distances.get(factory.id);
        }

        public int getRadius() {
            return FACTORY_RADIUS;
        }

        public int getCurrentProductionRate() {
            return (disabled == 0) ? this.productionRate : 0;
        }

        @Override
        public String toPlayerString(int playerIdx) {
            int ownerShip = 0;
            if (owner != null) {
                ownerShip = (playerIdx == owner.id) ? 1 : -1;
            }
            return toPlayerString(ownerShip, unitCount, this.productionRate, disabled, 0);
        }

        public String toViewStringInit() {
            return id + " " + this.productionRate + " " + position.x + " " + position.y + " " + getRadius();
        }

        public String toViewString() {
            return (owner == null ? "-1" : owner.id) + " " + unitCount + " " + this.productionRate + " " + disabled;
        }
    }

    private static abstract class MovingEntity extends Entity {
        protected Player owner;
        protected int remainingTurns;
        protected Factory source;
        protected Factory destination;

        public MovingEntity(EntityType type, Factory source, Factory destination) {
            super(type);
            this.owner = source.owner;
            this.source = source;
            this.destination = destination;
            this.remainingTurns = source.getDistanceTo(destination);
        }

        public void move() {
            this.remainingTurns--;
        }

        public <A extends MovingEntity> A findWithSameRouteInList(List<A> list) {
            for (A other : list) {
                if (other.source == this.source && other.destination == this.destination) {
                    return other;
                }
            }
            return null;
        }
    }

    private static class Bomb extends MovingEntity {
        public Bomb(Factory source, Factory destination) {
            super(EntityType.BOMB, source, destination);
        }

        @Override
        public String toPlayerString(int playerIdx) {
            if (owner.id == playerIdx) {
                return toPlayerString(1, source.id, destination.id, remainingTurns, 0);
            } else {
                return toPlayerString(-1, source.id, -1, -1, 0);
            }
        }

        public String toViewString() {
            return id + " " + (owner == null ? 0 : (owner.id)) + " " + source.id + " " + destination.id + " " + remainingTurns;
        }

        public void explode() {
            int damage = Math.min(destination.unitCount, Math.max(10, destination.unitCount / 2));
            destination.unitCount -= damage;
            destination.disabled = DAMAGE_DURATION;
        }
    }

    private static class Troop extends MovingEntity {
        private int unitCount;

        public Troop(Factory source, Factory destination, int unitCount) {
            super(EntityType.TROOP, source, destination);
            this.unitCount = unitCount;
        }

        @Override
        public String toPlayerString(int playerIdx) {
            int ownerShip = 0;
            if (owner != null) {
                ownerShip = (playerIdx == owner.id) ? 1 : -1;
            }
            return toPlayerString(ownerShip, source.id, destination.id, unitCount, remainingTurns);
        }

        public String toViewString() {
            return id + " " + (owner == null ? 0 : (owner.id)) + " " + source.id + " " + destination.id + " " + unitCount + " " + remainingTurns;
        }
    }

    @Override
    protected void initReferee(int playerCount, Properties prop) throws InvalidFormatException {
        this.seed = Long.valueOf(prop.getProperty("seed", String.valueOf(new Random(System.currentTimeMillis()).nextLong())));
        String factoryCount = prop.getProperty("factory_count");
        if (factoryCount != null) {
            this.customFactoryCount = Integer.valueOf(factoryCount);
        }
        String initialUnitCount = prop.getProperty("initial_unit_count");
        if (initialUnitCount != null) {
            this.customInitialUnitCount = Integer.valueOf(initialUnitCount);
        }

        newTroops = new ArrayList<>();
        newBombs = new ArrayList<>();

        this.random = new Random(seed);
        generatePlayers(playerCount);
        generateFactories();

        this.troops = new LinkedList<>();
        this.bombs = new LinkedList<>();

        for (Player player : players) {
            player.setTroops(troops);
            player.setFactories(factories);
        }
    }

    void generatePlayers(int playerCount) {
        this.players = new Player[playerCount];
        for (int i = 0; i < playerCount; i++) {
            this.players[i] = new Player(i);
        }
    }

    /**
     * Generate the factory objects
     */
    void generateFactories() {
        int factoryCount;
        if (customFactoryCount != null && customFactoryCount >= MIN_FACTORY_COUNT && customFactoryCount <= MAX_FACTORY_COUNT) {
            factoryCount = customFactoryCount;
        } else {
            factoryCount = MIN_FACTORY_COUNT + this.random.nextInt(MAX_FACTORY_COUNT - MIN_FACTORY_COUNT + 1);
        }

        if (factoryCount % 2 == 0) { // factoryCount must be odd
            factoryCount++;
        }
        FACTORY_RADIUS = factoryCount > 10 ? 600 : 700;

        int minSpaceBetweenFactories = 2 * (FACTORY_RADIUS + EXTRA_SPACE_BETWEEN_FACTORIES);

        this.factories = new Factory[factoryCount];
        
        int i = 0;

        // Add one factory at the center of the map
        this.factories[i++] = new Factory(null, WIDTH / 2, HEIGHT / 2, 0, 0);

        while (i < factoryCount - 1) {
            int x = random.nextInt(WIDTH / 2 - 2 * FACTORY_RADIUS) + FACTORY_RADIUS + EXTRA_SPACE_BETWEEN_FACTORIES;
            int y = random.nextInt(HEIGHT - 2 * FACTORY_RADIUS) + FACTORY_RADIUS + EXTRA_SPACE_BETWEEN_FACTORIES;

            boolean valid = true;
            for (int j = 0; j < i; j++) {
                Factory factory = this.factories[j];
                if (factory.position.distance(x, y) < minSpaceBetweenFactories) {
                    valid = false;
                    break;
                }
            }

            if (valid) {
                int productionRate = MIN_PRODUCTION_RATE + random.nextInt(MAX_PRODUCTION_RATE - MIN_PRODUCTION_RATE + 1);

                if (i == 1) {
                    int unitCount;
                    if (customInitialUnitCount != null && customInitialUnitCount >= PLAYER_INIT_UNITS_MIN
                            && customInitialUnitCount <= PLAYER_INIT_UNITS_MAX) {
                        unitCount = customInitialUnitCount;
                    } else {
                        unitCount = PLAYER_INIT_UNITS_MIN + random.nextInt(PLAYER_INIT_UNITS_MAX - PLAYER_INIT_UNITS_MIN + 1);
                    }
                    this.factories[i++] = new Factory(players[0], x, y, unitCount, productionRate);
                    this.factories[i++] = new Factory(players[1], WIDTH - x, HEIGHT - y, unitCount, productionRate);
                } else {
                    int unitCount = random.nextInt(5 * productionRate + 1);
                    this.factories[i++] = new Factory(null, x, y, unitCount, productionRate);
                    this.factories[i++] = new Factory(null, WIDTH - x, HEIGHT - y, unitCount, productionRate);
                }
            }
        }

        int totalProductionRate = 0;
        for (Factory factory : factories) {
            factory.computeDistances(this.factories);
            totalProductionRate += factory.productionRate;
        }
        
        // Make sure that the initial accumulated production rate for all the factories is at least MIN_TOTAL_PRODUCTION_RATE
        for (int j = 1; totalProductionRate < MIN_TOTAL_PRODUCTION_RATE && j < factories.length; j++) {
            if (factories[j].productionRate < MAX_PRODUCTION_RATE) {
                factories[j].productionRate++;
                totalProductionRate++;
            }
        }
    }

    @Override
    protected Properties getConfiguration() {
        Properties prop = new Properties();
        prop.setProperty("seed", String.valueOf(this.seed));
        if (this.customFactoryCount != null) {
            prop.setProperty("factory_count", String.valueOf(this.customFactoryCount));
        }
        if (this.customInitialUnitCount != null) {
            prop.setProperty("initial_unit_count", String.valueOf(this.customInitialUnitCount));
        }
        return prop;
    }

    @Override
    protected String[] getInitInputForPlayer(int playerIdx) {
        List<String> data = new ArrayList<>();
        data.add(String.valueOf(factories.length));

        // Factory distances
        List<String> links = new ArrayList<>();
        for (int i = 0; i < factories.length; i++) {
            for (int j = i + 1; j < factories.length; j++) {
                links.add(factories[i].id + " " + factories[j].id + " " + factories[i].getDistanceTo(factories[j]));
            }
        }
        data.add(String.valueOf(links.size()));
        data.addAll(links);

        return data.toArray(new String[data.size()]);
    }

    @Override
    protected void prepare(int round) {
    }

    @Override
    protected String[] getInputForPlayer(int round, int playerIdx) {
        List<String> data = new ArrayList<>();
        List<String> entities = new ArrayList<>();

        for (Factory factory : factories) {
            entities.add(factory.toPlayerString(playerIdx));
        }
        for (Troop troop : troops) {
            entities.add(troop.toPlayerString(playerIdx));
        }
        for (Bomb bomb : bombs) {
            entities.add(bomb.toPlayerString(playerIdx));
        }

        data.add(String.valueOf(entities.size()));
        data.addAll(entities);
        return data.toArray(new String[data.size()]);
    }

    @Override
    protected int getExpectedOutputLineCountForPlayer(int playerIdx) {
        return 1;
    }

    @Override
    protected void handlePlayerOutput(int frame, int round, int playerIdx, String[] outputs)
            throws WinException, LostException, InvalidInputException {

        Player player = this.players[playerIdx];
        player.lastBombActions.clear();
        player.lastIncActions.clear();
        player.lastMoveActions.clear();
        player.message = null;
        try {
            for (String line : outputs) {
                for (String action : PLAYER_INPUT_ACTION_SEPARATOR_PATTERN.split(line)) {
                    Matcher matchMove = PLAYER_INPUT_MOVE_PATTERN.matcher(action);
                    Matcher matchWait = PLAYER_INPUT_WAIT_PATTERN.matcher(action);
                    Matcher matchBomb = PLAYER_INPUT_BOMB_PATTERN.matcher(action);
                    Matcher matchInc = PLAYER_INPUT_INC_PATTERN.matcher(action);
                    Matcher matchMessage = PLAYER_INPUT_MSG_PATTERN.matcher(action);
                    if (matchMove.matches()) {
                        if (MOVE_RESTRICTION_ENABLED && !player.lastMoveActions.isEmpty()) {
                            // Silently ignore multiple moves
                            continue;
                        }

                        int src = Integer.parseInt(matchMove.group("src"));
                        int dst = Integer.parseInt(matchMove.group("dst"));
                        int units = Integer.parseInt(matchMove.group("units"));

                        if (src >= this.factories.length) {
                            throw new InvalidInputException("0 <= source < " + this.factories.length, String.valueOf(src));
                        }
                        if (dst >= this.factories.length) {
                            throw new InvalidInputException("0 <= destination < " + this.factories.length, String.valueOf(dst));
                        }
                        if (this.factories[src].owner != player) {
                            throw new LostException("MoveFromNotControlledFactory", src);
                        }
                        if (src == dst) {
                            throw new LostException("MoveSameSourceDestination", src);
                        }

                        player.lastMoveActions.add(new MoveAction(this.factories[src], this.factories[dst], units));
                    } else if (matchBomb.matches()) {
                        int src = Integer.parseInt(matchBomb.group("src"));
                        int dst = Integer.parseInt(matchBomb.group("dst"));
                        if (src >= this.factories.length) {
                            throw new InvalidInputException("0 <= source < " + this.factories.length, String.valueOf(src));
                        }
                        if (dst >= this.factories.length) {
                            throw new InvalidInputException("0 <= destination < " + this.factories.length, String.valueOf(dst));
                        }
                        if (this.factories[src].owner != player) {
                            throw new LostException("BombFromNotControlledFactory", src);
                        }
                        if (src == dst) {
                            throw new LostException("BombSameSourceDestination", src);
                        }

                        player.lastBombActions.add(new BombAction(this.factories[src], this.factories[dst]));
                    } else if (matchInc.matches()) {
                        if (!INCREASE_ACTION_ENABLED) {
                            // Silently ignore increase actions
                            continue;
                        }

                        int src = Integer.parseInt(matchInc.group("src"));
                        
                        if (src >= this.factories.length) {
                            throw new InvalidInputException("0 <= source < " + this.factories.length, String.valueOf(src));
                        }                        
                        if (this.factories[src].owner != player) {
                            throw new LostException("IncFromNotControlledFactory", src);
                        }

                        player.lastIncActions.add(new IncAction(this.factories[src]));
                    } else if (matchWait.matches()) {
                        // do nothing.
                    } else if (matchMessage.matches()) {
                        String message = matchMessage.group("message").trim();
                        if (message.length() > 100) {
                            message = message.substring(0, 100);
                        }
                        player.message = message;
                    } else {
                        throw new InvalidInputException("A valid action", action);
                    }
                }
            }
        } catch (InvalidInputException | LostException e) {
            player.setDead();
            throw e;
        }
    }

    @Override
    protected void updateGame(int round) throws GameOverException {
        newTroops.clear();
        newBombs.clear();

        // ---
        // Move troops and bombs
        // ---
        for (Troop troop : troops) {
            troop.move();
        }
        for (Bomb bomb : bombs) {
            bomb.move();
        }

        // ---
        // Decrease disabled countdown
        // ---
        for (Factory factory : factories) {
            if (factory.disabled > 0) {
                factory.disabled--;
            }
        }

        // ---
        // Execute orders
        // ---
        for (Player player : players) {
            // Send bombs
            for (BombAction bombAction : player.lastBombActions) {
                Bomb bomb = new Bomb(bombAction.src, bombAction.dst);
                if (player.remainingBombs > 0 && bomb.findWithSameRouteInList(newBombs) == null) {
                    newBombs.add(bomb);
                    bombs.add(bomb);
                    player.remainingBombs--;
                    addToolTip(player.id, translate("BombAction", player.id, bombAction.src.id, bombAction.dst.id));
                }
            }

            // Send troops
            for (MoveAction moveAction : player.lastMoveActions) {
                int unitsToMove = Math.min(moveAction.src.unitCount, moveAction.units);
                Troop troop = new Troop(moveAction.src, moveAction.dst, unitsToMove);

                if (unitsToMove > 0 && troop.findWithSameRouteInList(newBombs) == null) { // Forbid sending units with the same source and destination as a bomb
                    moveAction.src.unitCount -= unitsToMove;

                    Troop other = troop.findWithSameRouteInList(newTroops);
                    if (other != null) {
                        other.unitCount += unitsToMove;
                    } else {
                        troops.add(troop);
                        newTroops.add(troop);
                    }
                }
            }

            // Increase
            for (IncAction incAction : player.lastIncActions) {
                if (incAction.src.unitCount >= COST_INCREASE_PRODUCTION && incAction.src.productionRate < MAX_PRODUCTION_RATE) {
                    incAction.src.productionRate++;
                    incAction.src.unitCount -= COST_INCREASE_PRODUCTION;
                    addToolTip(player.id, translate("IncAction", player.id, incAction.src.id));
                }
            }
        }

        // ---
        // Create new units
        // ---
        for (Factory factory : factories) {
            if (factory.owner != null) {
                factory.unitCount += factory.getCurrentProductionRate();
            }
        }

        // ---
        // Solve battles
        // ---
        for (Factory factory : factories) {
            factory.unitsReadyToFight[0] = factory.unitsReadyToFight[1] = 0;
        }
        for (Iterator<Troop> it = troops.iterator(); it.hasNext();) {
            Troop troop = it.next();
            if (troop.remainingTurns <= 0) {
                troop.destination.unitsReadyToFight[troop.owner.id] += troop.unitCount;
                it.remove();
            }
        }
        for (Factory factory : factories) {
            // Units from both players fight first
            int units = Math.min(factory.unitsReadyToFight[0], factory.unitsReadyToFight[1]);
            factory.unitsReadyToFight[0] -= units;
            factory.unitsReadyToFight[1] -= units;

            // Remaining units fight on the factory
            for (Player player : players) {
                if (factory.owner == player) { // Allied
                    factory.unitCount += factory.unitsReadyToFight[player.id];
                } else { // Opponent
                    if (factory.unitsReadyToFight[player.id] > factory.unitCount) {
                        factory.owner = player;
                        factory.unitCount = factory.unitsReadyToFight[player.id] - factory.unitCount;
                    } else {
                        factory.unitCount -= factory.unitsReadyToFight[player.id];
                    }
                }
            }
        }

        // ---
        // Solve bombs
        // ---
        for (Iterator<Bomb> it = bombs.iterator(); it.hasNext();) {
            Bomb bomb = it.next();
            if (bomb.remainingTurns <= 0) {
                bomb.explode();
                it.remove();
            }
        }

        // ---
        // Update score
        // ---
        for (Player player : players) {
            player.score = 0;
        }
        for (Factory factory : factories) {
            if (factory.owner != null) {
                factory.owner.score += factory.unitCount;
            }
        }
        for (Troop troop : troops) {
            if (troop.owner != null) {
                troop.owner.score += troop.unitCount;
            }
        }

        // ---
        // Check end conditions
        // ---
        boolean gameOver = false;
        for (Player player : players) {
            if (player.score == 0) {
                int production = 0;
                for (Factory factory : factories) {
                    if (factory.owner == player) {
                        production += factory.productionRate;
                    }
                }
                if (production == 0) {
                    gameOver = true;
                } else {
                    // Keep playing until this player has produced some units
                    gameOver = false;
                    break;
                }
            }
        }

        if (gameOver) {
            throw new GameOverException("endReached");
        }
    }

    @Override
    protected void populateMessages(Properties p) {
        p.put("endReached", "End reached");
    }

    @Override
    protected String[] getInitDataForView() {
        List<String> data = new ArrayList<>();
        data.add(WIDTH + " " + HEIGHT + " " + factories.length + " " + BOMBS_PER_PLAYER);
        for (Factory factory : factories) {
            data.add(factory.toViewStringInit());
        }
        data.add(0, String.valueOf(data.size() + 1));
        return data.toArray(new String[data.size()]);
    }

    @Override
    protected String[] getFrameDataForView(int round, int frame, boolean keyFrame) {
        List<String> data = new ArrayList<>();
        // Pass the scores and messages
        for (int playerIdx = 0; playerIdx < players.length; ++playerIdx) {
            String playerInfo = String.valueOf(getScore(playerIdx)) + " " + players[playerIdx].remainingBombs;
            if (players[playerIdx].message != null) {
                playerInfo += " " + players[playerIdx].message;
            }
            data.add(playerInfo);
        }

        // Pass the troops
        List<String> troopData = new ArrayList<>();
        for (Troop troop : newTroops) {
            troopData.add(troop.toViewString());
        }
        data.add(String.valueOf(troopData.size()));
        data.addAll(troopData);

        // Pass the bombs
        List<String> bombData = new ArrayList<>();
        for (Bomb bomb : newBombs) {
            bombData.add(bomb.toViewString());
        }
        data.add(String.valueOf(bombData.size()));
        data.addAll(bombData);

        // Pass the factories
        for (Factory factory : factories) {
            data.add(factory.toViewString());
        }
        return data.toArray(new String[data.size()]);
    }

    @Override
    protected String getGameName() {
        return "GhostInTheCell";
    }

    @Override
    protected String getHeadlineAtGameStartForConsole() {
        return null;
    }

    @Override
    protected int getMinimumPlayerCount() {
        return 2;
    }

    @Override
    protected boolean showTooltips() {
        return true;
    }

    @Override
    protected String[] getPlayerActions(int playerIdx, int round) {
        return new String[0];
    }

    @Override
    protected boolean isPlayerDead(int playerIdx) {
        return false;
    }

    @Override
    protected String getDeathReason(int playerIdx) {
        return "$" + playerIdx + ": Eliminated!";
    }

    @Override
    protected int getScore(int playerIdx) {
        return players[playerIdx].score;
    }

    @Override
    protected String[] getGameSummary(int round) {
        return new String[0];
    }

    @Override
    protected void setPlayerTimeout(int frame, int round, int playerIdx) {
        players[playerIdx].setDead();
    }

    @Override
    protected int getMaxRoundCount(int playerCount) {
        return 200;
    }

    @Override
    protected int getMillisTimeForRound() {
        return 50;
    }

    public static void main(String... args) throws IOException {
        new Referee(System.in, System.out, System.err);
    }

    public Referee(InputStream is, PrintStream out, PrintStream err) throws IOException {
        super(is, out, err);

    }
}
