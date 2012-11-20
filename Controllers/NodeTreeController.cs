﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using NBTExplorer.Model;
using Substrate.Nbt;
using NBTExplorer.Windows;
using NBTExplorer.Vendor.MultiSelectTreeView;
using System.IO;

namespace NBTExplorer.Controllers
{
    public class MessageBoxEventArgs : EventArgs
    {
        public bool Cancel { get; set; }
        public string Message { get; set; }

        public MessageBoxEventArgs (string message)
        {
            Message = message;
        }
    }

    public class NodeTreeController
    {
        private TreeView _nodeTree;
        private MultiSelectTreeView _multiTree;

        private IconRegistry _iconRegistry;

        //private TagCompoundDataNode _rootData;
        private RootDataNode _rootData;

        public NodeTreeController (TreeView nodeTree)
        {
            _nodeTree = nodeTree;
            _multiTree = nodeTree as MultiSelectTreeView;

            InitializeIconRegistry();
            ShowVirtualRoot = true;

            //_rootData = new TagCompoundDataNode(new TagNodeCompound());
            _rootData = new RootDataNode();
            RefreshRootNodes();
        }

        public RootDataNode Root
        {
            get { return _rootData; }
        }

        public TreeView Tree
        {
            get { return _nodeTree; }
        }

        public bool ShowVirtualRoot { get; set; }

        public string VirtualRootDisplay
        {
            get { return _rootData.NodeDisplay; }
            set
            {
                _rootData.SetDisplayName(value);
                if (ShowVirtualRoot && _nodeTree.Nodes.Count > 0)
                    UpdateNodeText(_nodeTree.Nodes[0]);
            }
        }

        public event EventHandler SelectionInvalidated;

        protected virtual void OnSelectionInvalidated (EventArgs e)
        {
            if (SelectionInvalidated != null)
                SelectionInvalidated(this, e);
        }

        private void OnSelectionInvalidated ()
        {
            OnSelectionInvalidated(EventArgs.Empty);
        }

        public event EventHandler<MessageBoxEventArgs> ConfirmAction;

        protected virtual void OnConfirmAction (MessageBoxEventArgs e)
        {
            if (ConfirmAction != null)
                ConfirmAction(this, e);
        }

        private bool OnConfirmAction (string message)
        {
            MessageBoxEventArgs e = new MessageBoxEventArgs(message);
            OnConfirmAction(e);
            return !e.Cancel;
        }


        private TreeNode SelectedNode
        {
            get
            {
                if (_multiTree != null)
                    return _multiTree.SelectedNode;
                else
                    return _nodeTree.SelectedNode;
            }
        }

        #region Load / Save

        public void Save ()
        {
            foreach (TreeNode node in _nodeTree.Nodes) {
                DataNode dataNode = node.Tag as DataNode;
                if (dataNode != null)
                    dataNode.Save();
            }

            OnSelectionInvalidated();
        }

        public void OpenPaths (string[] paths)
        {
            _nodeTree.Nodes.Clear();

            foreach (string path in paths) {
                if (Directory.Exists(path)) {
                    DirectoryDataNode node = new DirectoryDataNode(path);
                    _nodeTree.Nodes.Add(CreateUnexpandedNode(node));
                }
                else if (File.Exists(path)) {
                    DataNode node = null;
                    foreach (var item in FileTypeRegistry.RegisteredTypes) {
                        if (item.Value.NamePatternTest(path))
                            node = item.Value.NodeCreate(path);
                    }

                    if (node != null) {
                        _nodeTree.Nodes.Add(CreateUnexpandedNode(node));
                    }
                }
            }

            if (_nodeTree.Nodes.Count > 0) {
                _nodeTree.Nodes[0].Expand();
            }

            OnSelectionInvalidated();
        }

        #endregion

        public void CreateNode (TreeNode node, TagType type)
        {
            if (node == null || !(node.Tag is DataNode))
                return;

            DataNode dataNode = node.Tag as DataNode;
            if (!dataNode.CanCreateTag(type))
                return;

            if (dataNode.CreateNode(type)) {
                node.Text = dataNode.NodeDisplay;
                RefreshChildNodes(node, dataNode);
                OnSelectionInvalidated();
            }
        }

        public void CreateNode (TagType type)
        {
            if (_nodeTree.SelectedNode == null)
                return;

            CreateNode(_nodeTree.SelectedNode, type);
        }

        public void DeleteNode (TreeNode node)
        {
            if (node == null || !(node.Tag is DataNode))
                return;

            DataNode dataNode = node.Tag as DataNode;
            if (!dataNode.CanDeleteNode)
                return;

            if (dataNode.DeleteNode()) {
                UpdateNodeText(node.Parent);
                node.Remove();

                RemoveNodeFromSelection(node);
                OnSelectionInvalidated();
            }
        }

        private bool DeleteNode (IList<TreeNode> nodes)
        {
            bool? elideChildren = null;
            if (!CanOperateOnNodesEx(nodes, Predicates.DeleteNodePred, out elideChildren))
                return false;

            if (elideChildren == true)
                nodes = ElideChildren(nodes);

            bool selectionModified = false;
            foreach (TreeNode node in nodes) {
                DataNode dataNode = node.Tag as DataNode;
                if (dataNode.DeleteNode()) {
                    UpdateNodeText(node.Parent);
                    node.Remove();

                    RemoveNodeFromSelection(node);
                    selectionModified = true;
                }
            }

            if (selectionModified)
                OnSelectionInvalidated();

            return true;
        }

        private bool RemoveNodeFromSelection (TreeNode node)
        {
            bool selectionModified = false;

            if (_multiTree != null) {
                if (_multiTree.SelectedNodes.Contains(node)) {
                    _multiTree.SelectedNodes.Remove(node);
                    selectionModified = true;
                }

                if (_multiTree.SelectedNode == node) {
                    _multiTree.SelectedNode = null;
                    selectionModified = true;
                }
            }

            if (_nodeTree.SelectedNode == node) {
                _nodeTree.SelectedNode = null;
                selectionModified = true;
            }

            return selectionModified;
        }

        public void DeleteSelection ()
        {
            if (_multiTree != null)
                DeleteNode(_multiTree.SelectedNodes);
            else
                DeleteNode(SelectedNode);
        }

        public void EditNode (TreeNode node)
        {
            if (node == null || !(node.Tag is DataNode))
                return;

            DataNode dataNode = node.Tag as DataNode;
            if (!dataNode.CanEditNode)
                return;

            if (dataNode.EditNode()) {
                node.Text = dataNode.NodeDisplay;
                OnSelectionInvalidated();
            }
        }

        public void EditSelection ()
        {
            EditNode(SelectedNode);
        }

        private void RenameNode (TreeNode node)
        {
            if (node == null || !(node.Tag is DataNode))
                return;

            DataNode dataNode = node.Tag as DataNode;
            if (!dataNode.CanRenameNode)
                return;

            if (dataNode.RenameNode()) {
                node.Text = dataNode.NodeDisplay;
                OnSelectionInvalidated();
            }
        }

        public void RenameSelection ()
        {
            RenameNode(SelectedNode);
        }

        private void RefreshNode (TreeNode node)
        {
            if (node == null || !(node.Tag is DataNode))
                return;

            DataNode dataNode = node.Tag as DataNode;
            if (!dataNode.CanRefreshNode)
                return;

            if (!OnConfirmAction("Refresh data anyway?"))
                return;

            if (dataNode.RefreshNode()) {
                RefreshChildNodes(node, dataNode);
                ExpandToEdge(node);
                OnSelectionInvalidated();
            }
        }

        public void RefreshSelection ()
        {
            RefreshNode(SelectedNode);
        }

        private void ExpandToEdge (TreeNode node)
        {
            if (node == null || !(node.Tag is DataNode))
                return;

            DataNode dataNode = node.Tag as DataNode;
            if (dataNode.IsExpanded) {
                if (!node.IsExpanded)
                    node.Expand();

                foreach (TreeNode child in node.Nodes)
                    ExpandToEdge(child);
            }
        }

        private void CopyNode (TreeNode node)
        {
            if (node == null || !(node.Tag is DataNode))
                return;

            DataNode dataNode = node.Tag as DataNode;
            if (!dataNode.CanCopyNode)
                return;

            dataNode.CopyNode();
        }

        private void CutNode (TreeNode node)
        {
            if (node == null || !(node.Tag is DataNode))
                return;

            DataNode dataNode = node.Tag as DataNode;
            if (!dataNode.CanCutNode)
                return;

            if (dataNode.CutNode()) {
                UpdateNodeText(node.Parent);
                node.Remove();

                RemoveNodeFromSelection(node);
                OnSelectionInvalidated();
            }
        }

        private void PasteNode (TreeNode node)
        {
            if (node == null || !(node.Tag is DataNode))
                return;

            DataNode dataNode = node.Tag as DataNode;
            if (!dataNode.CanPasteIntoNode)
                return;

            if (dataNode.PasteNode()) {
                node.Text = dataNode.NodeDisplay;
                RefreshChildNodes(node, dataNode);

                OnSelectionInvalidated();
            }
        }

        public void CopySelection ()
        {
            CopyNode(SelectedNode);
        }

        public void CutSelection ()
        {
            CutNode(SelectedNode);
        }

        public void PasteIntoSelection ()
        {
            PasteNode(SelectedNode);
        }

        public void MoveNodeUp (TreeNode node)
        {
            if (node == null)
                return;

            DataNode datanode = node.Tag as DataNode;
            if (datanode == null || !datanode.CanMoveNodeUp)
                return;

            if (datanode.ChangeRelativePosition(-1)) {
                RefreshChildNodes(node.Parent, datanode.Parent);
                OnSelectionInvalidated();
            }
        }

        public void MoveNodeDown (TreeNode node)
        {
            if (node == null)
                return;

            DataNode datanode = node.Tag as DataNode;
            if (datanode == null || !datanode.CanMoveNodeDown)
                return;

            if (datanode.ChangeRelativePosition(1)) {
                RefreshChildNodes(node.Parent, datanode.Parent);
                OnSelectionInvalidated();
            }
        }

        public void MoveSelectionUp ()
        {
            MoveNodeUp(SelectedNode);
        }

        public void MoveSelectionDown ()
        {
            MoveNodeDown(SelectedNode);
        }

        /*public void CreateRootNode (TagType type)
        {
            if (_rootData.CreateNode(type)) {
                RefreshRootNodes();
                UpdateUI(_rootData);
            }
        }*/

        public void ExpandNode (TreeNode node)
        {
            if (node == null || !(node.Tag is DataNode))
                return;

            if (node.IsExpanded)
                return;

            node.Nodes.Clear();

            DataNode backNode = node.Tag as DataNode;
            if (!backNode.IsExpanded)
                backNode.Expand();

            foreach (DataNode child in backNode.Nodes)
                node.Nodes.Add(CreateUnexpandedNode(child));
        }

        public void ExpandSelectedNode ()
        {
            ExpandNode(SelectedNode);
            if (SelectedNode != null)
                SelectedNode.Expand();
        }

        public void CollapseNode (TreeNode node)
        {
            if (node == null || !(node.Tag is DataNode))
                return;

            DataNode backNode = node.Tag as DataNode;
            if (backNode.IsModified)
                return;

            backNode.Collapse();

            node.Nodes.Clear();
            if (backNode.HasUnexpandedChildren)
                node.Nodes.Add(new TreeNode());
        }

        public void CollapseSelectedNode ()
        {
            CollapseNode(SelectedNode);
        }

        private TreeNode GetRootFromDataNodePath (DataNode node, out Stack<DataNode> hierarchy)
        {
            hierarchy = new Stack<DataNode>();
            while (node != null) {
                hierarchy.Push(node);
                node = node.Parent;
            }

            DataNode rootDataNode = hierarchy.Pop();
            TreeNode frontNode = null;
            foreach (TreeNode child in _nodeTree.Nodes) {
                if (child.Tag == rootDataNode)
                    frontNode = child;
            }

            return frontNode;
        }

        private TreeNode FindFrontNode (DataNode node)
        {
            Stack<DataNode> hierarchy;
            TreeNode frontNode = GetRootFromDataNodePath(node, out hierarchy);

            if (frontNode == null)
                return null;

            while (hierarchy.Count > 0) {
                if (!frontNode.IsExpanded) {
                    frontNode.Nodes.Add(new TreeNode());
                    frontNode.Expand();
                }

                DataNode childData = hierarchy.Pop();
                foreach (TreeNode childFront in frontNode.Nodes) {
                    if (childFront.Tag == childData) {
                        frontNode = childFront;
                        break;
                    }
                }
            }

            return frontNode;
        }

        public void CollapseBelow (DataNode node)
        {
            Stack<DataNode> hierarchy;
            TreeNode frontNode = GetRootFromDataNodePath(node, out hierarchy);

            if (frontNode == null)
                return;

            while (hierarchy.Count > 0) {
                if (!frontNode.IsExpanded)
                    return;

                DataNode childData = hierarchy.Pop();
                foreach (TreeNode childFront in frontNode.Nodes) {
                    if (childFront.Tag == childData) {
                        frontNode = childFront;
                        break;
                    }
                }
            }

            if (frontNode.IsExpanded)
                frontNode.Collapse();
        }

        public void SelectNode (DataNode node)
        {
            if (_multiTree != null) {
                _multiTree.SelectedNode = FindFrontNode(node);
            }
            else {
                _nodeTree.SelectedNode = FindFrontNode(node);
            }
        }

        public void RefreshRootNodes ()
        {
            if (ShowVirtualRoot) {
                _nodeTree.Nodes.Clear();
                _nodeTree.Nodes.Add(CreateUnexpandedNode(_rootData));
                return;
            }

            if (_rootData.HasUnexpandedChildren)
                _rootData.Expand();

            Dictionary<DataNode, TreeNode> currentNodes = new Dictionary<DataNode, TreeNode>();
            foreach (TreeNode child in _nodeTree.Nodes) {
                if (child.Tag is DataNode)
                    currentNodes.Add(child.Tag as DataNode, child);
            }

            _nodeTree.Nodes.Clear();
            foreach (DataNode child in _rootData.Nodes) {
                if (!currentNodes.ContainsKey(child))
                    _nodeTree.Nodes.Add(CreateUnexpandedNode(child));
                else
                    _nodeTree.Nodes.Add(currentNodes[child]);
            }

            //if (_nodeTree.Nodes.Count == 0 && _rootData.HasUnexpandedChildren) {
            //    _rootData.Expand();
            //    RefreshRootNodes();
            //}
        }

        public void RefreshChildNodes (TreeNode node, DataNode dataNode)
        {
            Dictionary<DataNode, TreeNode> currentNodes = new Dictionary<DataNode, TreeNode>();
            foreach (TreeNode child in node.Nodes) {
                if (child.Tag is DataNode)
                    currentNodes.Add(child.Tag as DataNode, child);
            }

            node.Nodes.Clear();
            foreach (DataNode child in dataNode.Nodes) {
                if (!currentNodes.ContainsKey(child))
                    node.Nodes.Add(CreateUnexpandedNode(child));
                else
                    node.Nodes.Add(currentNodes[child]);
            }

            //foreach (TreeNode child in node.Nodes)
            //    child.ContextMenuStrip = BuildNodeContextMenu(child.Tag as DataNode);

            if (node.Nodes.Count == 0 && dataNode.HasUnexpandedChildren) {
                ExpandNode(node);
                node.Expand();
            }
        }

        public void RefreshTreeNode (DataNode dataNode)
        {
            TreeNode node = FindFrontNode(dataNode);
            RefreshChildNodes(node, dataNode);
        }

        private void InitializeIconRegistry ()
        {
            _iconRegistry = new IconRegistry();
            _iconRegistry.DefaultIcon = 15;

            _iconRegistry.Register(typeof(TagByteDataNode), 0);
            _iconRegistry.Register(typeof(TagShortDataNode), 1);
            _iconRegistry.Register(typeof(TagIntDataNode), 2);
            _iconRegistry.Register(typeof(TagLongDataNode), 3);
            _iconRegistry.Register(typeof(TagFloatDataNode), 4);
            _iconRegistry.Register(typeof(TagDoubleDataNode), 5);
            _iconRegistry.Register(typeof(TagByteArrayDataNode), 6);
            _iconRegistry.Register(typeof(TagStringDataNode), 7);
            _iconRegistry.Register(typeof(TagListDataNode), 8);
            _iconRegistry.Register(typeof(TagCompoundDataNode), 9);
            _iconRegistry.Register(typeof(RegionChunkDataNode), 9);
            _iconRegistry.Register(typeof(DirectoryDataNode), 10);
            _iconRegistry.Register(typeof(RegionFileDataNode), 11);
            _iconRegistry.Register(typeof(CubicRegionDataNode), 11);
            _iconRegistry.Register(typeof(NbtFileDataNode), 12);
            _iconRegistry.Register(typeof(TagIntArrayDataNode), 14);
            _iconRegistry.Register(typeof(RootDataNode), 16);
        }

        private void UpdateNodeText (TreeNode node)
        {
            if (node == null || !(node.Tag is DataNode))
                return;

            DataNode dataNode = node.Tag as DataNode;
            node.Text = dataNode.NodeDisplay;
        }

        public bool CheckModifications ()
        {
            foreach (TreeNode node in _nodeTree.Nodes) {
                DataNode dataNode = node.Tag as DataNode;
                if (dataNode != null && dataNode.IsModified)
                    return true;
            }

            return false;
        }

        private TreeNode CreateUnexpandedNode (DataNode node)
        {
            TreeNode frontNode = new TreeNode(node.NodeDisplay);
            frontNode.ImageIndex = _iconRegistry.Lookup(node.GetType());
            frontNode.SelectedImageIndex = frontNode.ImageIndex;
            frontNode.Tag = node;
            //frontNode.ContextMenuStrip = BuildNodeContextMenu(node);

            if (node.HasUnexpandedChildren || node.Nodes.Count > 0)
                frontNode.Nodes.Add(new TreeNode());

            return frontNode;
        }

        #region Capability Checking

        #region Capability Predicates

        public delegate bool DataNodePredicate (DataNode dataNode, out GroupCapabilities caps);

        public static class Predicates
        {
            public static bool CreateByteNodePred (DataNode dataNode, out GroupCapabilities caps)
            {
                caps = GroupCapabilities.Single;
                return (dataNode != null) && dataNode.CanCreateTag(TagType.TAG_BYTE);
            }

            public static bool CreateShortNodePred (DataNode dataNode, out GroupCapabilities caps)
            {
                caps = GroupCapabilities.Single;
                return (dataNode != null) && dataNode.CanCreateTag(TagType.TAG_SHORT);
            }

            public static bool CreateIntNodePred (DataNode dataNode, out GroupCapabilities caps)
            {
                caps = GroupCapabilities.Single;
                return (dataNode != null) && dataNode.CanCreateTag(TagType.TAG_INT);
            }

            public static bool CreateLongNodePred (DataNode dataNode, out GroupCapabilities caps)
            {
                caps = GroupCapabilities.Single;
                return (dataNode != null) && dataNode.CanCreateTag(TagType.TAG_LONG);
            }

            public static bool CreateFloatNodePred (DataNode dataNode, out GroupCapabilities caps)
            {
                caps = GroupCapabilities.Single;
                return (dataNode != null) && dataNode.CanCreateTag(TagType.TAG_FLOAT);
            }

            public static bool CreateDoubleNodePred (DataNode dataNode, out GroupCapabilities caps)
            {
                caps = GroupCapabilities.Single;
                return (dataNode != null) && dataNode.CanCreateTag(TagType.TAG_DOUBLE);
            }

            public static bool CreateByteArrayNodePred (DataNode dataNode, out GroupCapabilities caps)
            {
                caps = GroupCapabilities.Single;
                return (dataNode != null) && dataNode.CanCreateTag(TagType.TAG_BYTE_ARRAY);
            }

            public static bool CreateIntArrayNodePred (DataNode dataNode, out GroupCapabilities caps)
            {
                caps = GroupCapabilities.Single;
                return (dataNode != null) && dataNode.CanCreateTag(TagType.TAG_INT_ARRAY);
            }

            public static bool CreateStringNodePred (DataNode dataNode, out GroupCapabilities caps)
            {
                caps = GroupCapabilities.Single;
                return (dataNode != null) && dataNode.CanCreateTag(TagType.TAG_STRING);
            }

            public static bool CreateListNodePred (DataNode dataNode, out GroupCapabilities caps)
            {
                caps = GroupCapabilities.Single;
                return (dataNode != null) && dataNode.CanCreateTag(TagType.TAG_LIST);
            }

            public static bool CreateCompoundNodePred (DataNode dataNode, out GroupCapabilities caps)
            {
                caps = GroupCapabilities.Single;
                return (dataNode != null) && dataNode.CanCreateTag(TagType.TAG_COMPOUND);
            }

            public static bool RenameNodePred (DataNode dataNode, out GroupCapabilities caps)
            {
                caps = dataNode.RenameNodeCapabilities;
                return (dataNode != null) && dataNode.CanRenameNode;
            }

            public static bool EditNodePred (DataNode dataNode, out GroupCapabilities caps)
            {
                caps = dataNode.EditNodeCapabilities;
                return (dataNode != null) && dataNode.CanEditNode;
            }

            public static bool DeleteNodePred (DataNode dataNode, out GroupCapabilities caps)
            {
                caps = dataNode.DeleteNodeCapabilities;
                return (dataNode != null) && dataNode.CanDeleteNode;
            }

            public static bool MoveNodeUpPred (DataNode dataNode, out GroupCapabilities caps)
            {
                caps = dataNode.ReorderNodeCapabilities;
                return (dataNode != null) && dataNode.CanMoveNodeUp;
            }

            public static bool MoveNodeDownPred (DataNode dataNode, out GroupCapabilities caps)
            {
                caps = dataNode.ReorderNodeCapabilities;
                return (dataNode != null) && dataNode.CanMoveNodeDown;
            }

            public static bool CutNodePred (DataNode dataNode, out GroupCapabilities caps)
            {
                caps = dataNode.CutNodeCapabilities;
                return (dataNode != null) && dataNode.CanCutNode;
            }

            public static bool CopyNodePred (DataNode dataNode, out GroupCapabilities caps)
            {
                caps = dataNode.CopyNodeCapabilities;
                return (dataNode != null) && dataNode.CanCopyNode;
            }

            public static bool PasteIntoNodePred (DataNode dataNode, out GroupCapabilities caps)
            {
                caps = dataNode.PasteIntoNodeCapabilities;
                return (dataNode != null) && dataNode.CanPasteIntoNode;
            }

            public static bool SearchNodePred (DataNode dataNode, out GroupCapabilities caps)
            {
                caps = dataNode.SearchNodeCapabilites;
                return (dataNode != null) && dataNode.CanSearchNode;
            }

            public static bool ReorderNodePred (DataNode dataNode, out GroupCapabilities caps)
            {
                caps = dataNode.ReorderNodeCapabilities;
                return (dataNode != null) && dataNode.CanReoderNode;
            }

            public static bool RefreshNodePred (DataNode dataNode, out GroupCapabilities caps)
            {
                caps = dataNode.RefreshNodeCapabilites;
                return (dataNode != null) && dataNode.CanRefreshNode;
            }
        }

        #endregion

        public bool CanOperateOnSelection (DataNodePredicate pred)
        {
            if (_multiTree != null)
                return CanOperateOnNodes(_multiTree.SelectedNodes, pred);
            else
                return CanOperateOnNodes(new List<TreeNode>() { _nodeTree.SelectedNode }, pred);
        }

        private bool CanOperateOnNodes (IList<TreeNode> nodes, DataNodePredicate pred)
        {
            bool? elideChildren = null;
            return CanOperateOnNodesEx(nodes, pred, out elideChildren);
        }

        private bool CanOperateOnNodesEx (IList<TreeNode> nodes, DataNodePredicate pred, out bool? elideChildren)
        {
            GroupCapabilities caps = GroupCapabilities.All;
            elideChildren = null;

            foreach (TreeNode node in nodes) {
                if (node == null || !(node.Tag is DataNode))
                    return false;

                DataNode dataNode = node.Tag as DataNode;
                GroupCapabilities dataCaps;
                if (!pred(dataNode, out dataCaps))
                    return false;

                caps &= dataCaps;

                bool elideChildrenFlag = (dataNode.DeleteNodeCapabilities & GroupCapabilities.ElideChildren) == GroupCapabilities.ElideChildren;
                if (elideChildren == null)
                    elideChildren = elideChildrenFlag;
                if (elideChildren != elideChildrenFlag)
                    return false;
            }

            if (nodes.Count > 1 && !SufficientCapabilities(nodes, caps))
                return false;

            return true;
        }

        private IList<TreeNode> ElideChildren (IList<TreeNode> nodes)
        {
            List<TreeNode> filtered = new List<TreeNode>();

            foreach (TreeNode node in nodes) {
                TreeNode parent = node.Parent;
                bool foundParent = false;

                while (parent != null) {
                    if (nodes.Contains(parent)) {
                        foundParent = true;
                        break;
                    }
                    parent = parent.Parent;
                }

                if (!foundParent)
                    filtered.Add(node);
            }

            return filtered;
        }

        private bool CommonContainer (IEnumerable<TreeNode> nodes)
        {
            object container = null;
            foreach (TreeNode node in nodes) {
                DataNode dataNode = node.Tag as DataNode;
                if (container == null)
                    container = dataNode.Parent;

                if (container != dataNode.Parent)
                    return false;
            }

            return true;
        }

        private bool CommonType (IEnumerable<TreeNode> nodes)
        {
            Type datatype = null;
            foreach (TreeNode node in nodes) {
                DataNode dataNode = node.Tag as DataNode;
                if (datatype == null)
                    datatype = dataNode.GetType();

                if (datatype != dataNode.GetType())
                    return false;
            }

            return true;
        }

        private bool SufficientCapabilities (IEnumerable<TreeNode> nodes, GroupCapabilities commonCaps)
        {
            bool commonContainer = CommonContainer(nodes);
            bool commonType = CommonType(nodes);

            bool pass = true;
            if (commonContainer && commonType)
                pass &= ((commonCaps & GroupCapabilities.SiblingSameType) == GroupCapabilities.SiblingSameType);
            else if (commonContainer && !commonType)
                pass &= ((commonCaps & GroupCapabilities.SiblingMixedType) == GroupCapabilities.SiblingMixedType);
            else if (!commonContainer && commonType)
                pass &= ((commonCaps & GroupCapabilities.MultiSameType) == GroupCapabilities.MultiSameType);
            else if (!commonContainer && !commonType)
                pass &= ((commonCaps & GroupCapabilities.MultiMixedType) == GroupCapabilities.MultiMixedType);

            return pass;
        }

        #endregion
    }
}
