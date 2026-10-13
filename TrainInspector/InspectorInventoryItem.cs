using System;
using System.Collections.Generic;
using System.Reflection;
using DV;
using DV.CabControls;
using DV.CabControls.Spec;
using DV.InventorySystem;
using DV.ThingTypes;
using DV.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TrainInspector
{
    /// <summary>
    /// Registers and supplies a native inventory item without adding a custom asset bundle.
    /// The stock clipboard supplies the mesh, colliders, grab poses, and inventory icons.
    /// </summary>
    public sealed class InspectorInventoryItemManager : MonoBehaviour
    {
        internal const string PrefabName = "TrainInspector_InspectionClipboard";
        internal const string DisplayName = "Inspection Clipboard";

        private const string SourcePrefabName = "Clipboard";
        private const float EnsureIntervalSeconds = 3f;

        private static readonly FieldInfo ItemPrefabNameField = typeof(InventoryItemSpec).GetField(
            "itemPrefabName", BindingFlags.Instance | BindingFlags.NonPublic);

        private static GameObject sourcePrefabObject;

        internal static InspectorInventoryItemManager Instance { get; private set; }

        private float nextEnsureTime;
        private StartingItemsController cachedStartingItems;
        private bool warnedMissingSource;
        private bool warnedInvalidSource;
        private bool warnedInventoryFull;

        private void Awake()
        {
            Instance = this;
            SceneManager.sceneLoaded += OnSceneLoaded;
            TryRegisterPrefab();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            nextEnsureTime = 0f;
            cachedStartingItems = null;
            warnedInventoryFull = false;
        }

        private void Update()
        {
            if (!Main.IsEnabled)
            {
                return;
            }

            if (Time.unscaledTime < nextEnsureTime)
            {
                return;
            }

            nextEnsureTime = Time.unscaledTime + EnsureIntervalSeconds;
            EnsureClipboardInInventory();
        }

        internal static bool EnsureClipboardNow()
        {
            if (Instance != null)
            {
                return Instance.EnsureClipboardInInventory();
            }

            return false;
        }

        internal bool EnsureClipboardInInventory()
        {
            if (!TryRegisterPrefab())
            {
                return false;
            }

            Transform player = null;
            try
            {
                player = PlayerManager.PlayerTransform;
            }
            catch
            {
            }

            if (player == null)
            {
                return false;
            }

            Inventory inventory = SingletonBehaviour<Inventory>.Instance;
            if (inventory == null)
            {
                return false;
            }

            if (HasInspectionClipboard(inventory))
            {
                warnedInventoryFull = false;
                return true;
            }

            // The clipboard is normally present, so avoid a scene-wide object search during the
            // recurring safety check. We only need the starting-items controller while recovering
            // a missing clipboard, and cache it for the rest of the scene.
            if (cachedStartingItems == null)
            {
                cachedStartingItems = FindObjectOfType<StartingItemsController>();
            }

            if (cachedStartingItems != null && !cachedStartingItems.itemsLoaded)
            {
                return false;
            }

            CleanStrayClipboards(inventory);

            if (!inventory.HasFreeSlots())
            {
                if (!warnedInventoryFull)
                {
                    warnedInventoryFull = true;
                    string key = Main.Settings != null ? Main.Settings.ToggleKeyDisplay : "F7";
                    Main.ModEntry.Logger.Warning(
                        "Inspection Clipboard could not be added because the inventory is full. Free an inventory slot or press " + key + ".");
                }

                return false;
            }

            AddClipboardToInventory(inventory);
            return true;
        }

        private bool TryRegisterPrefab()
        {
            if (sourcePrefabObject != null)
            {
                return true;
            }

            ItemsConfig itemsConfig = Globals.G != null ? Globals.G.Items : null;
            List<InventoryItemSpec> items = itemsConfig != null ? itemsConfig.items : null;
            if (items == null)
            {
                return false;
            }

            InventoryItemSpec source = FindPrefab(items, SourcePrefabName);
            if (source == null)
            {
                source = FindClipboardPrefab(items);
            }

            if (source == null)
            {
                if (!warnedMissingSource)
                {
                    warnedMissingSource = true;
                    string key = Main.Settings != null ? Main.Settings.ToggleKeyDisplay : "F7";
                    Main.ModEntry.Logger.Warning(
                        "The stock Clipboard prefab was not found, so the physical Inspection Clipboard is unavailable. " +
                        key + " still opens Train Inspector.");
                }

                return false;
            }

            if (ItemPrefabNameField == null)
            {
                Main.ModEntry.Logger.Error(
                    "InventoryItemSpec.itemPrefabName was not found; the Inspection Clipboard cannot be registered.");
                return false;
            }

            // ItemsConfig contains configuration objects, not necessarily the actual inventory prefab.
            // Match StartingItemsController's stock path and load the prefab by its registered resource name.
            GameObject resourcePrefab = Resources.Load<GameObject>(source.ItemPrefabName);
            if (resourcePrefab == null)
            {
                if (!warnedMissingSource)
                {
                    warnedMissingSource = true;
                    Main.ModEntry.Logger.Warning(
                        "The stock Clipboard resource prefab could not be loaded, so the physical Inspection " +
                        "Clipboard is unavailable. F7 still opens Train Inspector.");
                }

                return false;
            }

            bool hasInventorySpec = resourcePrefab.GetComponent<InventoryItemSpec>() != null;
            bool hasItemSpec = resourcePrefab.GetComponent<Item>() != null;
            if (!hasInventorySpec || !hasItemSpec)
            {
                if (!warnedInvalidSource)
                {
                    warnedInvalidSource = true;
                    string key = Main.Settings != null ? Main.Settings.ToggleKeyDisplay : "F7";
                    Main.ModEntry.Logger.Error(
                        "The loaded stock Clipboard prefab is missing required components " +
                        "(InventoryItemSpec=" + hasInventorySpec + ", Item=" + hasItemSpec +
                        "). " + key + " remains available.");
                }

                return false;
            }

            sourcePrefabObject = resourcePrefab;
            Main.ModEntry.Logger.Log(
                "Prepared the physical Inspection Clipboard using Derail Valley's stock Clipboard resource prefab.");
            return true;
        }

        private static InventoryItemSpec FindPrefab(IEnumerable<InventoryItemSpec> items, string prefabName)
        {
            foreach (InventoryItemSpec item in items)
            {
                if (item != null && string.Equals(item.ItemPrefabName, prefabName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }

            return null;
        }

        private static InventoryItemSpec FindClipboardPrefab(IEnumerable<InventoryItemSpec> items)
        {
            foreach (InventoryItemSpec item in items)
            {
                if (item == null)
                {
                    continue;
                }

                string prefabName = item.ItemPrefabName ?? string.Empty;
                string objectName = item.gameObject != null ? item.gameObject.name : string.Empty;
                if ((prefabName.IndexOf("clipboard", StringComparison.OrdinalIgnoreCase) >= 0
                     || objectName.IndexOf("clipboard", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return item;
                }
            }

            return null;
        }

        internal static bool HasInspectionClipboard(Inventory inventory)
        {
            InspectorInventoryItem.ActiveInstances.RemoveWhere(inst => inst == null);

            foreach (InspectorInventoryItem instance in InspectorInventoryItem.ActiveInstances)
            {
                if (instance != null && instance.IsGrabbed)
                {
                    return true;
                }
            }

            if (inventory == null)
            {
                return false;
            }

            int capacity = inventory.Capacity;
            for (int i = 0; i < capacity; i++)
            {
                GameObject item = inventory.PeekItemAtSlot(i, true);
                if (item != null && (item.GetComponent<InspectorInventoryItem>() != null
                    || string.Equals(item.name, PrefabName, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        private static void CleanStrayClipboards(Inventory inventory)
        {
            List<InspectorInventoryItem> all = new List<InspectorInventoryItem>(InspectorInventoryItem.ActiveInstances);
            for (int i = 0; i < all.Count; i++)
            {
                InspectorInventoryItem instance = all[i];
                if (instance != null && !instance.IsGrabbed && (inventory == null || !IsItemInInventory(inventory, instance.gameObject)))
                {
                    Destroy(instance.gameObject);
                }
            }

            InspectorInventoryItem.ActiveInstances.RemoveWhere(inst => inst == null);
        }

        private static bool IsItemInInventory(Inventory inventory, GameObject target)
        {
            if (inventory == null || target == null)
            {
                return false;
            }

            int capacity = inventory.Capacity;
            for (int i = 0; i < capacity; i++)
            {
                GameObject slotItem = inventory.PeekItemAtSlot(i, true);
                if (slotItem == target)
                {
                    return true;
                }
            }

            return false;
        }

        private void AddClipboardToInventory(Inventory inventory)
        {
            if (sourcePrefabObject == null)
            {
                return;
            }

            // Clone the untouched resource prefab while inactive. This lets ConfigureObject run before
            // ItemBase.Awake reads its Item spec and avoids cloning runtime-only components from a template.
            bool sourceWasActive = sourcePrefabObject.activeSelf;
            GameObject clipboard;
            try
            {
                if (sourceWasActive)
                {
                    sourcePrefabObject.SetActive(false);
                }

                clipboard = Instantiate(sourcePrefabObject, new Vector3(0f, -10000f, 0f), Quaternion.identity);
            }
            finally
            {
                if (sourceWasActive)
                {
                    sourcePrefabObject.SetActive(true);
                }
            }

            clipboard.hideFlags = HideFlags.None;
            clipboard.name = PrefabName;
            clipboard.transform.SetParent(null, false);
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid())
            {
                SceneManager.MoveGameObjectToScene(clipboard, activeScene);
            }

            ConfigureObject(clipboard);
            if (clipboard.GetComponent<InspectorInventoryItem>() == null)
            {
                clipboard.AddComponent<InspectorInventoryItem>();
            }

            clipboard.SetActive(true);

            int slot = inventory.AddItemToInventory(clipboard);
            if (slot < 0)
            {
                Destroy(clipboard);
                return;
            }

            warnedInventoryFull = false;
            Main.ModEntry.Logger.Log(
                "Added the Inspection Clipboard to inventory slot " + (slot + 1) + ". Equip it to open Train Inspector.");
        }

        internal static void ConfigureObject(GameObject itemObject)
        {
            if (itemObject == null)
            {
                return;
            }

            InventoryItemSpec inventorySpec = itemObject.GetComponent<InventoryItemSpec>();
            if (inventorySpec != null)
            {
                if (ItemPrefabNameField != null)
                {
                    ItemPrefabNameField.SetValue(inventorySpec, PrefabName);
                }

                inventorySpec.NameOverride = DisplayName;
                inventorySpec.BelongsToPlayer = true;
                inventorySpec.IsEssential = true;
                inventorySpec.ImmuneToDumpster = true;
            }

            Item itemSpec = itemObject.GetComponent<Item>();
            if (itemSpec != null)
            {
                itemSpec.itemUseApproach = ItemUseApproach.OneShot;
                itemSpec.excludeFromStorageSerialization = StorageType.Inventory
                                                        | StorageType.LostAndFound
                                                        | StorageType.World
                                                        | StorageType.InstalledGadgets
                                                        | StorageType.ItemContainers;
            }
        }
    }

    /// <summary>Opens the inspection UI when the physical clipboard is equipped or used.</summary>
    public sealed class InspectorInventoryItem : MonoBehaviour, IInventoryItemLocalizer
    {
        internal static readonly HashSet<InspectorInventoryItem> ActiveInstances = new HashSet<InspectorInventoryItem>();

        private ItemBase item;
        private bool subscribed;
        private bool ownsVisibleWindow;

        internal bool IsGrabbed { get; private set; }

        internal static bool IsClipboardOpen
        {
            get
            {
                ActiveInstances.RemoveWhere(inst => inst == null);
                foreach (InspectorInventoryItem instance in ActiveInstances)
                {
                    if (instance != null && (instance.IsGrabbed || instance.ownsVisibleWindow))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        internal static void NotifyWindowClosed()
        {
            ActiveInstances.RemoveWhere(inst => inst == null);
            foreach (InspectorInventoryItem instance in ActiveInstances)
            {
                if (instance != null)
                {
                    instance.ownsVisibleWindow = false;
                }
            }
        }

        private void Awake()
        {
            ActiveInstances.Add(this);
            InspectorInventoryItemManager.ConfigureObject(gameObject);
            EnsureSubscribed();
        }

        private void Start()
        {
            ActiveInstances.Add(this);
            EnsureSubscribed();
        }

        private void OnEnable()
        {
            ActiveInstances.Add(this);
            EnsureSubscribed();
            InventoryItemSpec spec = GetComponent<InventoryItemSpec>();
            if (spec != null)
            {
                spec.NameOverride = InspectorInventoryItemManager.DisplayName;
            }
        }

        private void OnDisable()
        {
            IsGrabbed = false;
            ActiveInstances.Remove(this);
            if (ownsVisibleWindow)
            {
                Main.SetInspectorWindowVisible(false);
                ownsVisibleWindow = false;
            }
        }

        private bool EnsureSubscribed()
        {
            if (subscribed)
            {
                return true;
            }

            if (item == null)
            {
                item = GetComponent<ItemBase>();
            }

            if (item == null)
            {
                Item spec = GetComponent<Item>();
                if (spec != null && ControlsInstantiatorBase.Instance != null)
                {
                    ControlsInstantiatorBase.Instance.Spawn(spec);
                    item = GetComponent<ItemBase>();
                }
            }

            if (item == null)
            {
                return false;
            }

            item.Grabbed += OnGrabbed;
            item.Ungrabbed += OnUngrabbed;
            item.Used += OnUsed;
            subscribed = true;
            return true;
        }

        private void OnDestroy()
        {
            IsGrabbed = false;
            ActiveInstances.Remove(this);
            if (ownsVisibleWindow)
            {
                Main.SetInspectorWindowVisible(false);
                ownsVisibleWindow = false;
            }

            if (!subscribed || item == null)
            {
                return;
            }

            item.Grabbed -= OnGrabbed;
            item.Ungrabbed -= OnUngrabbed;
            item.Used -= OnUsed;
            subscribed = false;
        }

        private void OnGrabbed(ControlImplBase _)
        {
            IsGrabbed = true;
            if (!Main.IsEnabled)
            {
                return;
            }

            bool wasVisible = Main.IsInspectorWindowVisible;
            Main.SetInspectorWindowVisible(true);
            ownsVisibleWindow = !wasVisible;
        }

        private void OnUngrabbed(ControlImplBase _)
        {
            IsGrabbed = false;
            if (ownsVisibleWindow)
            {
                Main.SetInspectorWindowVisible(false);
                ownsVisibleWindow = false;
            }
        }

        private void OnUsed()
        {
            if (!Main.IsEnabled)
            {
                return;
            }

            bool show = !Main.IsInspectorWindowVisible;
            Main.SetInspectorWindowVisible(show);
            ownsVisibleWindow = show;
        }

        public string GetNameParam()
        {
            return string.Empty;
        }

        public string GetCustomDescription()
        {
            string key = Main.Settings != null ? Main.Settings.ToggleKeyDisplay : "F7";
            return "Equip or use this clipboard to open Train Inspector. Press " + key + " at any time as a fallback.";
        }
    }
}
