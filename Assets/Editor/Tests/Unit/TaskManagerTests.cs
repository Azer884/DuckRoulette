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
        private HashSet<Challenge> _registered;
        private List<Challenge> _registeredSnapshot;
        private readonly List<Challenge> _created = new();
        private Random.State _randomState;

        [SetUp]
        public void SetUp()
        {
            Assume.That(NetworkManager.Singleton, Is.Null);
            _randomState = Random.state;
            Random.InitState(42);

            _registered = Reflect.Get<HashSet<Challenge>>(typeof(TaskManager), "registeredObjectives");
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
            Object.DestroyImmediate(_go);

            _registered.Clear();
            foreach (Challenge c in _registeredSnapshot) _registered.Add(c);

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

        private List<int> Pick() => (List<int>)Reflect.Call(_tm, "PickTasksForPlayer");

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
        public void DistributeTasks_WhenNotServer_DealsNothing()
        {
            _tm.tasks = new[] { Make("A"), Make("B") };
            _tm.DistributeTasks(new List<ulong> { 1, 2 });
            Assert.That(Assigned.Count, Is.EqualTo(0));
        }

        [Test]
        public void PickTasks_DistinctRegisteredOnly_UpToTasksPerRound()
        {
            Challenge unregistered = Make("NoObjectiveInLevel", register: false);
            _tm.tasks = new[] { Make("A"), Make("B"), unregistered, Make("C"), Make("D") };
            int unregisteredIndex = 2;

            for (int i = 0; i < 100; i++)
            {
                List<int> picked = Pick();
                Assert.That(picked, Has.Count.EqualTo(3));
                Assert.That(picked, Is.Unique);
                Assert.That(picked, Has.No.Member(unregisteredIndex));
                Assert.That(picked, Has.All.InRange(0, 4));
            }
        }

        [Test]
        public void PickTasks_PoolSmallerThanQuota_ReturnsWholePool()
        {
            _tm.tasks = new[] { Make("A"), null, Make("B") };
            List<int> picked = Pick();
            Assert.That(picked, Is.EquivalentTo(new[] { 0, 2 }));
        }

        [Test]
        public void PickTasks_NoTasksAuthored_ReturnsEmpty()
        {
            _tm.tasks = new Challenge[0];
            LogAssert.Expect(LogType.Warning, "TaskManager: no tasks authored, nobody will get any.");
            Assert.That(Pick(), Is.Empty);
        }

        [Test]
        public void PickTasks_EveryTaskEventuallyOffered()
        {
            _tm.tasks = new[] { Make("A"), Make("B"), Make("C"), Make("D"), Make("E") };
            var seen = new HashSet<int>();
            for (int i = 0; i < 100; i++) seen.UnionWith(Pick());
            Assert.That(seen, Is.EquivalentTo(Enumerable.Range(0, 5)));
        }

        [Test]
        public void RegisterObjective_NullIgnored_UnregisterRemoves()
        {
            Assert.DoesNotThrow(() => TaskManager.RegisterObjective(null));
            Assert.DoesNotThrow(() => TaskManager.UnregisterObjective(null));

            Challenge a = Make("A");
            Assert.That(_registered, Has.Member(a));
            TaskManager.UnregisterObjective(a);
            Assert.That(_registered, Has.No.Member(a));
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
            var entry = new TaskManager.TaskEntry { ClientId = ulong.MaxValue - 1, TaskIndex = 7, Completed = true };
            Assert.That(entry.Equals(entry), Is.True);
            Assert.That(entry.Equals(new TaskManager.TaskEntry { ClientId = entry.ClientId, TaskIndex = 7, Completed = false }), Is.False);

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
