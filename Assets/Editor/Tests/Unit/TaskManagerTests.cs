using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace DuckRoulette.Tests.Unit
{
    [TestFixture, Category("Unit")]
    public class TaskManagerTests
    {
        private GameObject _go;
        private TaskManager _tm;
        private Dictionary<Challenge, int> _registered;
        private List<KeyValuePair<Challenge, int>> _registeredSnapshot;
        private readonly List<Challenge> _created = new();
        private Random.State _randomState;

        [SetUp]
        public void SetUp()
        {
            Assume.That(NetworkManager.Singleton, Is.Null);
            _randomState = Random.state;
            Random.InitState(42);

            _registered = Reflect.Get<Dictionary<Challenge, int>>(typeof(TaskManager), "registeredObjectives");
            _registeredSnapshot = _registered.ToList();
            _registered.Clear();

            _go = new GameObject("TaskManager (unit test)") { hideFlags = HideFlags.DontSave };
            _tm = _go.AddComponent<TaskManager>();
        }

        [TearDown]
        public void TearDown()
        {
            Random.state = _randomState;

            // Awake/OnDestroy never run in edit mode, so the NetworkList's native buffer is ours to free.
            Reflect.Get<NetworkList<TaskManager.TaskEntry>>(_tm, "assignedTasks")?.Dispose();
            Reflect.Get<NetworkList<int>>(_tm, "doneInWorld")?.Dispose();
            Object.DestroyImmediate(_go);

            _registered.Clear();
            foreach (var pair in _registeredSnapshot) _registered[pair.Key] = pair.Value;

            foreach (Challenge c in _created) Object.DestroyImmediate(c);
            _created.Clear();
        }

        private Challenge Make(string name, Challenge.TaskType type = Challenge.TaskType.Useful, bool register = true)
        {
            var challenge = ScriptableObject.CreateInstance<Challenge>();
            challenge.name = name;
            challenge.taskName = name;
            challenge.taskType = type;
            _created.Add(challenge);
            if (register) TaskManager.RegisterObjective(challenge);
            return challenge;
        }

        private NetworkList<TaskManager.TaskEntry> Assigned => Reflect.Get<NetworkList<TaskManager.TaskEntry>>(_tm, "assignedTasks");

        private int PickSolo(int excluded = -1) => (int)Reflect.Call(_tm, "PickSoloTask", 1UL, excluded);
        private int PickGroup() => (int)Reflect.Call(_tm, "PickGroupTask");

        [Test]
        public void HasCompletedAllTasks_NothingAssigned_True()
        {
            Assert.That(_tm.HasCompletedAllTasks(3), Is.True);
        }

        [Test]
        public void HasCompletedAllTasks_OpenTaskOnlyBlocksItsOwner()
        {
            Assigned.Add(new TaskManager.TaskEntry { ClientId = 1, TaskIndex = 0, Completed = true });
            Assigned.Add(new TaskManager.TaskEntry { ClientId = 1, TaskIndex = 2, Completed = false });
            Assigned.Add(new TaskManager.TaskEntry { ClientId = 2, TaskIndex = 1, Completed = true });

            Assert.That(_tm.HasCompletedAllTasks(1), Is.False);
            Assert.That(_tm.HasCompletedAllTasks(2), Is.True);
            Assert.That(_tm.HasCompletedAllTasks(99), Is.True);
        }

        [Test]
        public void CompleteTaskForPlayer_WhenNotServer_IsIgnored()
        {
            Challenge a = Make("A");
            _tm.tasks = new[] { a };
            Reflect.Call(_tm, "RebuildTaskIndices");
            Assigned.Add(new TaskManager.TaskEntry { ClientId = 1, TaskIndex = 0, Completed = false });

            _tm.CompleteTaskForPlayer(1, a);

            Assert.That(_tm.HasCompletedAllTasks(1), Is.False);
        }

        [Test]
        public void OnGunHandedOff_WhenNotServer_ChangesNothing()
        {
            _tm.tasks = new[] { Make("A") };
            Assigned.Add(new TaskManager.TaskEntry { ClientId = 1, TaskIndex = 0, Completed = true });
            _tm.OnGunHandedOff();
            Assert.That(Assigned.Count, Is.EqualTo(1));
        }

        [Test]
        public void PickSolo_RegisteredNonGroupOnly()
        {
            Challenge unregistered = Make("NoObjectiveInLevel", register: false);
            Challenge group = Make("Group", Challenge.TaskType.ThreePlus);
            _tm.tasks = new[] { Make("A"), unregistered, group, Make("B", Challenge.TaskType.Useless) };

            var seen = new HashSet<int>();
            for (int i = 0; i < 100; i++) seen.Add(PickSolo());
            Assert.That(seen, Is.EquivalentTo(new[] { 0, 3 }));
        }

        [Test]
        public void PickSolo_SkipsTasksSomeoneElseHoldsOpen_AndTheExcludedOne()
        {
            _tm.tasks = new[] { Make("A"), Make("B"), Make("C") };
            Assigned.Add(new TaskManager.TaskEntry { ClientId = 2, TaskIndex = 0, Completed = false });
            Assigned.Add(new TaskManager.TaskEntry { ClientId = 3, TaskIndex = 1, Completed = true });

            var seen = new HashSet<int>();
            for (int i = 0; i < 100; i++) seen.Add(PickSolo(excluded: 2));
            Assert.That(seen, Is.EquivalentTo(new[] { 1 }), "open tasks are never shared; finished ones are free again");
        }

        [Test]
        public void PickSolo_NothingAvailable_ReturnsMinusOne()
        {
            _tm.tasks = new[] { Make("A") };
            Assert.That(PickSolo(excluded: 0), Is.EqualTo(-1));
            _tm.tasks = new Challenge[0];
            Assert.That(PickSolo(), Is.EqualTo(-1));
        }

        [Test]
        public void PickGroup_OnlyRegisteredThreePlusNotAlreadyOpen()
        {
            _tm.tasks = new[] { Make("A"), Make("G1", Challenge.TaskType.ThreePlus), Make("G2", Challenge.TaskType.ThreePlus),
                Make("G3", Challenge.TaskType.ThreePlus, register: false) };
            Assigned.Add(new TaskManager.TaskEntry { ClientId = 1, TaskIndex = 2, GroupId = 5, Completed = false });

            for (int i = 0; i < 50; i++) Assert.That(PickGroup(), Is.EqualTo(1));
        }

        [Test]
        public void RegisterObjective_NullIgnored_RefCounted()
        {
            Assert.DoesNotThrow(() => TaskManager.RegisterObjective(null));
            Assert.DoesNotThrow(() => TaskManager.UnregisterObjective(null));

            Challenge a = Make("A");
            TaskManager.RegisterObjective(a);
            Assert.That(TaskManager.IsObjectiveRegistered(a), Is.True);
            TaskManager.UnregisterObjective(a);
            Assert.That(TaskManager.IsObjectiveRegistered(a), Is.True, "a second objective for the same task is still live");
            TaskManager.UnregisterObjective(a);
            Assert.That(TaskManager.IsObjectiveRegistered(a), Is.False);
        }

        [Test]
        public void GetTask_BoundsChecked()
        {
            Challenge a = Make("A"), b = Make("B");
            _tm.tasks = new[] { a, b };

            Assert.That(_tm.GetTask(1), Is.SameAs(b));
            Assert.That(_tm.GetTask(-1), Is.Null);
            Assert.That(_tm.GetTask(2), Is.Null);

            _tm.tasks = null;
            Assert.That(_tm.GetTask(0), Is.Null);
        }

        [Test]
        public void LocalPlayerQueries_WithoutNetworkManager_AreFalseOrEmpty()
        {
            Challenge a = Make("A");
            _tm.tasks = new[] { a };
            Reflect.Call(_tm, "RebuildTaskIndices");
            Assigned.Add(new TaskManager.TaskEntry { ClientId = 0, TaskIndex = 0, Completed = false });

            Assert.That(_tm.IsTaskOpenForLocalPlayer(a), Is.False);
            Assert.That(_tm.IsTaskCompletedByLocalPlayer(a), Is.False);
            var results = new List<TaskManager.TaskEntry> { default };
            _tm.GetLocalPlayerTasks(results);
            Assert.That(results, Is.Empty);
            Assert.DoesNotThrow(() => _tm.ReportTaskCompleted(a));
        }

        [Test]
        public void TaskEntry_EqualityAndNetworkSerializationRoundTrip()
        {
            var entry = new TaskManager.TaskEntry { ClientId = ulong.MaxValue - 1, TaskIndex = 7, Completed = true, GroupId = 3, RoundsOpen = 2 };
            Assert.That(entry.Equals(entry), Is.True);
            Assert.That(entry.Equals(new TaskManager.TaskEntry { ClientId = entry.ClientId, TaskIndex = 7, Completed = false, GroupId = 3, RoundsOpen = 2 }), Is.False);
            Assert.That(entry.Equals(new TaskManager.TaskEntry { ClientId = entry.ClientId, TaskIndex = 7, Completed = true, GroupId = 4, RoundsOpen = 2 }), Is.False);

            using var writer = new FastBufferWriter(64, Allocator.Temp);
            writer.WriteNetworkSerializable(entry);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out TaskManager.TaskEntry back);

            Assert.That(back.Equals(entry), Is.True);
        }

        [Test]
        public void Challenge_LabelsFallBackWhenUnauthored()
        {
            Challenge c = Make("AssetName", register: false);
            c.taskName = "  ";
            c.interactionVerb = "";
            Assert.That(c.DisplayName, Is.EqualTo("AssetName"));
            Assert.That(c.InteractionPrompt, Is.EqualTo("Use"));

            c.taskName = "Campfire";
            c.interactionVerb = "Light";
            Assert.That(c.DisplayName, Is.EqualTo("Campfire"));
            Assert.That(c.InteractionPrompt, Is.EqualTo("Light"));
        }

        [Test]
        public void AuthoredPlayerPrefab_TaskListHasNoNullsOrDuplicates()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            Assert.That(prefab, Is.Not.Null);
            TaskManager authored = prefab.GetComponentInChildren<TaskManager>(true);
            Assume.That(authored, Is.Not.Null, "Player.prefab carries no TaskManager");

            Assert.That(authored.tasks, Is.Not.Null.And.Not.Empty);
            Assert.That(authored.tasks, Has.None.Null, "null slot in TaskManager.tasks");
            Assert.That(authored.tasks, Is.Unique, "the same Challenge is listed twice; its index would be ambiguous");
        }
    }
}
