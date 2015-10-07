﻿using UnityEngine;
using System.Collections;
using UnityEditor;
using System.Collections.Generic;
using System;
using System.Xml;
using System.Runtime.Serialization;
using System.IO;
using DialogSystem;

public class DialogEditor : EditorWindow
{
    public DialogCollection sourceCollection;

    private GUIStyle headerStyle;
    private Color inspectorColor;
    private GUIStyle buttonStyle;
    private GUIStyle lblStyle;

    private const string txtNotSetMsg = "Text not set";

    private HashSet<int> reservedDialogIDs = new HashSet<int>();
    private int ReserveDialogID()
    {
        int newID = 0;
        while (reservedDialogIDs.Contains(newID))
        {
            newID += 1;
        }
        reservedDialogIDs.Add(newID);
        return newID;
    }
    private void ReleaseDialogID(int id)
    {
        reservedDialogIDs.Remove(id);
    }

    private void LoadFixReservedIDs(List<Dialog> allDialogs)
    {
        bool needsSaving = false;
        for (int i = 0; i < allDialogs.Count; i++)
        {
            List<Dialog> subDialogs = new List<Dialog>();
            subDialogs = GetAllDialogsInChain(subDialogs, allDialogs[i]);
            for (int s = 0; s < subDialogs.Count; s++)
            {
                if (!reservedDialogIDs.Contains(subDialogs[s].ID))
                {
                    reservedDialogIDs.Add(subDialogs[s].ID);
                }
                else
                {
                    subDialogs[s].ID = ReserveDialogID();
                    needsSaving = true;
                }
            }
        }
        if (needsSaving)
        {
            Debug.LogWarning("duplicate IDs fixed, Save Dialog file!");
        }
    }

    public void Cleanup()
    {
        if (sourceCollection != null)
        {
            EditorUtility.SetDirty(sourceCollection);
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(sourceCollection));
        }
        if (activeStringEditor != null) 
        {
            activeStringEditor.EndEdit();
            activeStringEditor = null;
        }
    }

    private Dialog activeDialog;
    private DialogOption activeOptionNode;
    private Dialog activeSubDialog;
    private void InspectOptionNode(DialogOption o)
    {
        activeSubDialog = null;
        activeOptionNode = o;
    }
    private void InspectDialogNode(Dialog d)
    {
        activeOptionNode = null;
        activeSubDialog = d;
    }
    private void CloseSubInspector()
    {
        activeSubDialog = null;
        activeOptionNode = null;
        activeStringEditor = null;
    }
    private float leftColumnWidth = 200;
    private float rightColumnoverlayWidth = 200;
    private Color nodeColor = new Color(0.75f,0.75f,0.75f,1f);
    Vector2 dialogsScroll = Vector2.zero;

    private LocalizedStringEditor activeStringEditor;

    void OnGUI()
    {
        if (sourceCollection == null) { return; }
        if (activeStringEditor != null) { GUI.enabled = false; }
        else { GUI.enabled = (activeOptionNode == null && activeSubDialog == null); }
        Rect Left = new Rect(0, 0, leftColumnWidth, position.height);
        Color prev = GUI.color;
        GUI.color = inspectorColor;
        GUI.BeginGroup(Left, EditorStyles.textField);
        GUI.color = prev;
        DisplayDialogTools(Left);
        GUI.EndGroup();
        DisplayDialogEditor(new Rect(leftColumnWidth, 0, position.width-leftColumnWidth, position.height));
        if (activeOptionNode != null)
        {
            GUI.enabled = true & activeStringEditor == null;
            DisplayOptionNodeInspector(new Rect(position.width - rightColumnoverlayWidth, 0, rightColumnoverlayWidth, position.height), activeOptionNode);
        }
        else if (activeSubDialog != null)
        {
            GUI.enabled = true & activeStringEditor == null;
            DisplayDialogNodeInspector(new Rect(position.width - rightColumnoverlayWidth, 0, rightColumnoverlayWidth, position.height), activeSubDialog);
        }
        if (activeStringEditor != null)
        {
            GUI.enabled = true;
            if (activeStringEditor.DrawGUI() == false)
            {
                activeStringEditor.EndEdit();
                activeStringEditor = null;
            }
        }
    }

    public void Initialize()
    {
        if (headerStyle == null) 
        {
            headerStyle = new GUIStyle(GUI.skin.GetStyle("flow shader node 0")); 
            headerStyle.stretchWidth = true; 
            headerStyle.fontStyle = FontStyle.Bold; 
            headerStyle.fontSize = 14;
            headerStyle.normal.textColor = new Color(0.7f, 0.6f, 0.6f);
        }
        lblStyle = new GUIStyle(GUI.skin.GetStyle("flow overlay header upper left"));
        lblStyle.stretchWidth = true;
        inspectorColor = Color.Lerp(Color.gray, Color.white, 0.5f);
        buttonStyle = GUI.skin.GetStyle("PreButton");
    }

    void Update()
    {
        if (sourceCollection == null)
        {
            Cleanup();
            Close();
            return;
        }
    }

    void OnDestroy()
    {
        Cleanup();
    }

    bool isValidName(string name)
    {
        return !string.IsNullOrEmpty(name) && !name.StartsWith(" ");
    }

    public static void OpenEdit(DialogCollection collection)
    {
        DialogEditor window = EditorWindow.GetWindow<DialogEditor>("Dialog Editor");
        window.minSize = new Vector2(600, 400);
        window.Initialize();
        window.sourceCollection = collection;
    }

    private void DeleteDialog(Dialog d)
    {
        if (sourceCollection.dialogs.Remove(d))
        {
            DestroyImmediate(d, true);
            DirtyAsset();
        }
    }

    private void DirtyAsset()
    {
        EditorUtility.SetDirty(sourceCollection);
    }

    private void AddToAsset(ScriptableObject so)
    {
        so.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
        AssetDatabase.AddObjectToAsset(so, sourceCollection);
        DirtyAsset();
    }

    private void DeleteCleanupDialog(Dialog toDelete)
    {
        DeleteRecurseDialog(toDelete, activeDialog, false);
        ReleaseDialogID(toDelete.ID);
    }

    private void DeleteRecurseDialog(Dialog toDelete, Dialog currentItemToCheck, bool releaseIfNotLoop)
    {
        if (currentItemToCheck == null) { return; }
        for (int i = currentItemToCheck.Options.Count; i-- > 0; )
        {
            if (currentItemToCheck.Options[i].NextDialog == null) { continue; }

            if (currentItemToCheck.Options[i].NextDialog == toDelete | releaseIfNotLoop)
            {
                if (!currentItemToCheck.Options[i].IsRedirection)
                {
                    DeleteRecurseDialog(toDelete, currentItemToCheck.Options[i].NextDialog, true);
                }
                currentItemToCheck.Options[i].NextDialog = null; //TODO cleanup unreferenced assets
            }
            else
            {
                DeleteRecurseDialog(toDelete, currentItemToCheck.Options[i].NextDialog, false);
            }
        }
    }

    void DisplayDialogTools(Rect r)
    {
        GUILayout.Space(5);
        GUILayout.BeginVertical(EditorStyles.textArea,GUILayout.Width(r.width-8));
        if (GUILayout.Button("New Dialog"))
        {
            Dialog d = ScriptableObject.CreateInstance<Dialog>();
            AddToAsset(d);
            d.ID = ReserveDialogID();
            sourceCollection.dialogs.Add(d);
            activeDialog = d;
        }
        GUILayout.EndVertical();
        Rect itemRect = new Rect(0, 0, r.width - 38, 17);
        Rect deleteItemRect = new Rect(itemRect.width, 0, 18, 17);
        Rect scrollContainerRect = new Rect(2, 30, r.width - 5, r.height - 32);
        Rect contentScrollRect = new Rect(0, 0, r.width - 20, Mathf.Max(sourceCollection.dialogs.Count * itemRect.height, scrollContainerRect.height));
        dialogsScroll = GUI.BeginScrollView(scrollContainerRect, dialogsScroll, contentScrollRect, false, true);
        for (int i = 0; i < sourceCollection.dialogs.Count; i++)
        {
            itemRect.y = i * (itemRect.height+2);
            deleteItemRect.y = itemRect.y;
            if (itemRect.y < dialogsScroll.y - itemRect.height)
            {
                continue;
            }
            if (itemRect.y > dialogsScroll.y + scrollContainerRect.height)
            {
                break;
            }
            if (sourceCollection.dialogs[i] == activeDialog)
            {
                GUI.color = Color.gray;
            }
            string dTitle = sourceCollection.dialogs[i].Title.Description;
            if (dTitle == null || dTitle.Length == 0)
            {
                dTitle = txtNotSetMsg;
            }
            if (GUI.Button(itemRect, dTitle, "ButtonLeft"))
            {
                if (activeDialog == sourceCollection.dialogs[i]) { activeDialog = null; }
                else
                {
                    activeDialog = sourceCollection.dialogs[i];
                }
            }
            GUI.color = Color.white;
            if (GUI.Button(deleteItemRect, "x", "ButtonRight"))
            {
                DeleteCleanupDialog(sourceCollection.dialogs[i]);
                if (sourceCollection.dialogs[i] == activeDialog) { CloseSubInspector(); activeDialog = null; }
                DeleteDialog(sourceCollection.dialogs[i]);
            }
        }
        GUI.EndScrollView();
    }

    Vector2 mainViewScrollPos;
    float nodeWidth = 180;
    float nodeHeight = 30;
    float indentWidth = 50;
    void DisplayDialogEditor(Rect r)
    {
        if (activeDialog == null) { return; }
        GUI.color = nodeColor;
        GUILayout.BeginArea(new Rect(r.x, r.y, r.width, r.height));
        if (countedHeight < r.height) { countedHeight = r.height-15; }
        if (countedWidth < r.width) { countedWidth = r.width-15; }
        float inspectorInset = 0;
        if (activeOptionNode != null || activeSubDialog != null)
        {
            inspectorInset = rightColumnoverlayWidth;
            if (countedWidth > r.width)
            {
                mainViewScrollPos = new Vector2(mainViewScrollPos.x + inspectorInset, mainViewScrollPos.y);
            }
        }
        mainViewScrollPos = GUI.BeginScrollView(new Rect(0, 0, r.width-inspectorInset, r.height), mainViewScrollPos, new Rect(0, 0, countedWidth, countedHeight));
        if (previousState != countedWidth + countedHeight)
        {
            Repaint();
            previousState = countedWidth + countedHeight;
        }
        countedHeight = 0;
        countedWidth = 0;
        RecurseDialogs(new Rect(5, r.y+5,nodeWidth, nodeHeight) , 0, 0, activeDialog);
        GUI.EndScrollView();
        GUILayout.EndArea();
        GUI.color = Color.white;
    }

    float countedWidth;
    float countedHeight;
    float previousState;
    int RecurseDialogs(Rect parent, int length, int depth, Dialog d)
    {
        Rect r = new Rect(parent.x + (depth * nodeWidth), parent.y + (length * nodeHeight), nodeWidth, nodeHeight);
        DisplaySingleDialogNode(r, d, length, depth);
        int branchDepth = 1;
        int lastOptionDepth = 0;
        for (int i = 0; i < d.Options.Count;i++ )
        {
            int move = 0;
            if (d.Options[i].NextDialog != null)
            {
                if (d.Options[i].IsRedirection)
                {
                    lastOptionDepth = branchDepth;
                    move = DisplayDialogOption(new Rect(r.x + nodeWidth + indentWidth, r.y + nodeHeight * lastOptionDepth, nodeWidth + indentWidth, nodeHeight), d.Options[i]);
                    if (d.Options[i].NextDialog == null) { continue; }
                    DisplayDialogLoop(new Rect(r.x + nodeWidth + indentWidth * 2, r.y + nodeHeight * lastOptionDepth, nodeWidth + indentWidth, nodeHeight), d.Options[i]);
                    branchDepth += 1;
                }
                else
                {
                    lastOptionDepth = branchDepth;
                    move = DisplayDialogOption(new Rect(r.x + nodeWidth + indentWidth, r.y + nodeHeight * lastOptionDepth, nodeWidth + indentWidth, nodeHeight), d.Options[i]);
                    if (d.Options[i].NextDialog == null) { continue; }
                    branchDepth += RecurseDialogs(new Rect(parent.x + indentWidth * 2, parent.y, nodeWidth, nodeHeight), length + branchDepth, depth + 1, d.Options[i].NextDialog);
                }
            }
            else
            {
                lastOptionDepth = branchDepth;
                move = DisplayDialogOption(new Rect(r.x + nodeWidth + indentWidth, r.y + nodeHeight * lastOptionDepth, nodeWidth + indentWidth, nodeHeight), d.Options[i]);
                branchDepth += 1;
            }
            if (move != 0)
            {
                if (move == -10)
                {
                    if (d.Options.Count > 0)
                    {
                        DialogOption dI = d.Options[i];
                        if (dI.NextDialog != null && !dI.IsRedirection)
                        {
                            DeleteCleanupDialog(dI.NextDialog);
                        }
                        d.Options.Remove(dI);
                        DestroyImmediate(dI, true);
                        DirtyAsset();
                    }
                }
                else if (i + move >= 0 & i + move < d.Options.Count)
                {
                    DialogOption dO = d.Options[i];
                    d.Options[i] = d.Options[i + move];
                    d.Options[i + move] = dO;
                }
            }
        }
        if (DrawOptionRegion(new Rect(r.x + nodeWidth + indentWidth, r.y + nodeHeight, nodeWidth + indentWidth, nodeHeight), lastOptionDepth))
        {
            DialogOption dO = ScriptableObject.CreateInstance<DialogOption>();
            AddToAsset(dO);
            d.Options.Add(dO);
        }
        return branchDepth;
    }

    int DisplayDialogOption(Rect r, DialogOption option)
    {
        Color prev = GUI.color;
        GUI.color = nodeColor;
        GUIStyle gs = new GUIStyle(GUI.skin.button);
        gs.alignment = TextAnchor.MiddleLeft;
        gs.fontSize = 8;
        gs.border = new RectOffset(2, 2, 2, 2);
        Rect title = new Rect(r.x - r.width + indentWidth+1, r.y + r.height * 0.6f, r.width, r.height * 0.4f);
        if (title.y + title.height > countedHeight) { countedHeight = title.y + title.height; }
        if (title.x + title.width > countedWidth) { countedWidth = title.x + title.width; }
        int ret = 0;
        string tTitle = option.Text.Description;
        if (tTitle == null || tTitle.Length == 0)
        {
            tTitle = txtNotSetMsg;
        }
        if (GUI.Button(title, tTitle, gs))
        {
            InspectOptionNode(option);
        }
        gs.alignment = TextAnchor.MiddleCenter;
        if (GUI.Button(new Rect(title.x - 18, title.y - 5, 17, 17), "˄", gs))
        {
            ret = -1;
        }
        if (GUI.Button(new Rect(title.x - 34, title.y - 5, 17, 17), "˅", gs))
        {
            ret = 1;
        }
        if (GUI.Button(new Rect(title.x - 50, title.y - 5, 17, 17), "x", gs))
        {
            ret = -10;
        }
        if (option.NextDialog != null)
        {
            DrawInlineDialogConstraints(new Rect(title.x+indentWidth, title.y - (title.height+2), title.width, title.height+10), option);
            DrawInlineDialogNotifications(new Rect(title.x + indentWidth, title.y + title.height, title.width, title.height + 10), option);
            if (GUI.Button(new Rect(title.x + title.width - 17, r.y+5, 17, 13), "x", gs))
            {
                if (!option.IsRedirection)
                {
                    DeleteCleanupDialog(option.NextDialog);
                }
                option.NextDialog = null;
                option.IsRedirection = false;
            }
        }
        GUI.color = prev;
        return ret;
    }

    void DrawInlineDialogConstraints(Rect r, DialogOption d)
    {
        GUILayout.BeginArea(r);
        GUILayout.BeginHorizontal();
        Color prev = GUI.color;
        GUI.color = Color.white;
        GUIStyle gs = new GUIStyle(GUI.skin.button);
        gs.fontSize = 8;
        gs.alignment = TextAnchor.MiddleCenter;
        gs.contentOffset = new Vector2(-1, 0);
        gs.clipping = TextClipping.Overflow;
        gs.border = new RectOffset(1, 1, 1, 1);
        if (d.NextDialog != null)
        {
            for (int i = d.NextDialog.Requirements.Count; i-- > 0; )
            {
                DialogRequirement req = d.NextDialog.Requirements[i];
                GUI.color = req.GetColor();
                string tooltip = string.Format("Target: {0}, Type: {1}, IntValue({2}), StringValue('{3}'), FloatValue({4})", req.Target, req.Type, req.IntValue, req.StringValue, req.FloatValue);
                GUILayout.Box(new GUIContent(req.ShortIdentifier, tooltip), gs, GUILayout.Width(19), GUILayout.Height(15));
            }
        }
        GUI.color = prev;
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    void DrawInlineDialogNotifications(Rect r, DialogOption d)
    {
        GUILayout.BeginArea(r);
        GUILayout.BeginHorizontal();
        Color prev = GUI.color;
        GUI.color = Color.white;
        GUIStyle gs = new GUIStyle(GUI.skin.button);
        gs.fontSize = 8;
        gs.alignment = TextAnchor.MiddleCenter;
        gs.contentOffset = new Vector2(-1, 0);
        gs.clipping = TextClipping.Overflow;
        gs.border = new RectOffset(1, 1, 1, 1);
        if (d.NextDialog != null)
        {
            for (int i = d.Notifications.Count; i-- > 0; )
            {
                DialogOptionNotification don = d.Notifications[i];
                GUI.color = don.GetColor();
                string tooltip = string.Format("Target: {0}, Type: {1}, Value: {2}", don.Target, don.Type, don.Value);
                GUILayout.Box(new GUIContent(don.ShortIdentifier, tooltip), gs, GUILayout.Width(19), GUILayout.Height(15));
            }
        }
        GUI.color = prev;
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    bool DrawOptionRegion(Rect r, int depth)
    {
        bool ret = false;
        Color prev = GUI.color;
        GUI.color = nodeColor;
        if (depth > 0)
        {
            GUI.Box(new Rect(r.x - r.width + indentWidth - 2, r.y, 5, r.height * depth), "", EditorStyles.textArea);
        }
        if (GUI.Button(new Rect(r.x-r.width+indentWidth+2, r.y, 20, 19), "+", EditorStyles.miniButton)) {
            ret = true;
        }
        if (r.height + 17 > countedHeight) { countedHeight = r.height + 17; }
        GUI.color = prev;
        return ret;
    }

    Vector2 scrollbar;
    Vector2 scrollbar2;
    void DisplayOptionNodeInspector(Rect r, DialogOption dOption)
    {
        Color prev = GUI.color;
        GUI.color = inspectorColor;
        GUILayout.BeginArea(r, EditorStyles.textArea);
        GUI.color = prev;
        GUILayout.Space(5);
        GUILayout.Label("Text", headerStyle);
        string tText = dOption.Text.Description;
        if (tText == null || tText.Length == 0)
        {
            tText = txtNotSetMsg;
        }
        GUILayout.Label(tText, lblStyle);
        GUILayout.Space(5);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Edit", buttonStyle))
        {
            activeStringEditor = new LocalizedStringEditor(dOption.Text, "Option text");
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(5);
        GUILayout.Label(new GUIContent("Tag", "User data"), headerStyle);
        GUILayout.Space(5);
        dOption.Tag = GUILayout.TextField(dOption.Tag);
        GUILayout.Space(10);
        GUILayout.Label("Connection", headerStyle);
        GUILayout.Space(5);
        if (GUILayout.Button("New Sub-Dialog", buttonStyle))
        {
            int id = ReserveDialogID();
            Dialog d = ScriptableObject.CreateInstance<Dialog>();
            AddToAsset(d);
            d.ID = id;
            dOption.NextDialog = d;
            dOption.IsRedirection = false;
            CloseSubInspector();
        }
        GUILayout.Label(new GUIContent("Or link to existing: ", "Use with care, can result in infinite loops!"));
        scrollbar = GUILayout.BeginScrollView(scrollbar);
        List<Dialog> ddialogs = new List<Dialog>();
        ddialogs = GetAllDialogsInChain(ddialogs, activeDialog);
        for (int i = 0; i < ddialogs.Count; i++)
        {
            if (ddialogs[i].Options.Contains(dOption)) { continue; }
            string dTitle = ddialogs[i].Title.Description;
            if (dTitle == null || dTitle.Length == 0)
            {
                dTitle = txtNotSetMsg;
            }
            if (GUILayout.Button(dTitle, buttonStyle))
            {
                dOption.NextDialog = ddialogs[i];
                dOption.IsRedirection = true;
                CloseSubInspector();
                break;
            }
        }
        GUILayout.EndScrollView();
        GUILayout.Space(10);
        GUILayout.Label("Notifications", headerStyle);
        GUILayout.BeginHorizontal();
        bool prevEnabled = GUI.enabled;
        if (dOption.Notifications.Count >= 6) { GUI.enabled = false; }
        if (GUILayout.Button("Add", buttonStyle))
        {
            dOption.Notifications.Add(new DialogOptionNotification());
            DirtyAsset();
        }
        GUI.enabled = prevEnabled;
        if (GUILayout.Button("Remove all", buttonStyle))
        {
            dOption.Notifications.Clear();
        }
        GUILayout.EndHorizontal();
        scrollbar2 = GUILayout.BeginScrollView(scrollbar2);
        for (int i = dOption.Notifications.Count; i-- > 0; )
        {
            if (!InlineDisplayNotificationEditor(dOption.Notifications[i]))
            {
                dOption.Notifications.RemoveAt(i);
            }
        }
        GUILayout.EndScrollView();
        if (GUILayout.Button("Close", buttonStyle))
        {
            CloseSubInspector();
        }
        GUILayout.EndArea();
    }

    bool InlineDisplayNotificationEditor(DialogOptionNotification notification)
    {
        bool ret = true;
        GUILayout.BeginVertical(EditorStyles.textArea);
        if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(17)))
        {
            ret = false;
        }
        GUILayout.BeginHorizontal();
        GUILayout.Label("Type: ", GUILayout.Width(70));
        notification.Type = (DialogNotificationType)EditorGUILayout.EnumPopup(notification.Type);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Target: ", GUILayout.Width(70));
        notification.Target = (DialogNotificationTarget)EditorGUILayout.EnumPopup(notification.Target);
        GUILayout.EndHorizontal();

        if (notification.Target == DialogNotificationTarget.Other)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Name: ", "Exact name of target GameObject"), GUILayout.Width(50));
            notification.TargetName = EditorGUILayout.TextField(notification.TargetName);
            GUILayout.EndHorizontal();
        }

        if (notification.Type == DialogNotificationType.Other)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Value: ", GUILayout.Width(50));
            notification.Value = GUILayout.TextField(notification.Value);
            GUILayout.EndHorizontal();
        }

        GUILayout.EndVertical();
        return ret;
    }

    void DisplayDialogNodeInspector(Rect r, Dialog d)
    {
        Color prev = GUI.color;
        GUI.color = inspectorColor;
        GUILayout.BeginArea(r, EditorStyles.textArea);
        GUI.color = prev;
        GUILayout.Space(5);
        GUILayout.Label("Title", headerStyle);
        string dTitle = d.Title.Description;
        if (dTitle == null || dTitle.Length == 0)
        {
            dTitle = txtNotSetMsg;
        }
        GUILayout.Label(dTitle, lblStyle);
        GUILayout.Space(5);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Edit", buttonStyle))
        {
            activeStringEditor = new LocalizedStringEditor(d.Title, "Dialog title");
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(5);
        GUILayout.Label("Text", headerStyle);
        string dText = d.Text.Description;
        if (dText == null || dText.Length == 0)
        {
            dText = txtNotSetMsg;
        }
        GUILayout.Label(dText, lblStyle);
        GUILayout.Space(5);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Edit", buttonStyle))
        {
            activeStringEditor = new LocalizedStringEditor(d.Text, "Dialog text");
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(5);
        GUILayout.Label(new GUIContent("Tag", "User data"), headerStyle);
        GUILayout.Space(5);
        d.Tag = GUILayout.TextField(d.Tag);
        GUILayout.Space(10);
        GUILayout.Label("Requirements", headerStyle);
        if (d.Requirements.Count > 1)
        {
            d.RequirementMode = (Dialog.DialogRequirementMode)EditorGUILayout.EnumPopup("Mode:",d.RequirementMode);
        }
        GUILayout.BeginHorizontal();
        bool prevEnabled = GUI.enabled;
        if (d.Requirements.Count >= 6) { GUI.enabled = false; }
        if (GUILayout.Button("Add", buttonStyle))
        {
            d.Requirements.Add(new DialogRequirement());
        }
        GUI.enabled = prevEnabled;
        if (GUILayout.Button("Remove all", buttonStyle))
        {
            d.Requirements.Clear();
        }
        GUILayout.EndHorizontal();
        scrollbar = GUILayout.BeginScrollView(scrollbar, false, true);
        for (int i = d.Requirements.Count; i-- > 0; )
        {
            if (!DrawInlineRequirement(d.Requirements[i]) | d.Requirements[i].Type == DialogRequirementType.None) {
                d.Requirements.RemoveAt(i); 
                continue;
            }
        }
        GUILayout.EndScrollView();
        if (GUILayout.Button("Close", buttonStyle))
        {
            RemoveDuplicateRequirements(d.Requirements);
            CloseSubInspector();
        }
        GUILayout.EndArea();
    }

    void RemoveDuplicateRequirements(List<DialogRequirement> sourceList)
    {
        List<DialogRequirement> cleanList = new List<DialogRequirement>();
        for (int i = 0; i < sourceList.Count; i++)
        {
            bool found = false;
            for (int cl = 0; cl < cleanList.Count; cl++)
            {
                if (cleanList[cl].Type == sourceList[i].Type & cleanList[cl].Target == sourceList[i].Target)
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                cleanList.Add(sourceList[i]);
            }
        }
        sourceList.Clear();
        sourceList.AddRange(cleanList);
    }

    bool DrawInlineRequirement(DialogRequirement dr)
    {
        bool ret = true;
        GUILayout.BeginVertical(EditorStyles.textArea);
        if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(16))) { ret = false; }
        GUILayout.BeginHorizontal();
        GUILayout.Label("Type: ", GUILayout.Width(50));
        dr.Type = (DialogRequirementType)EditorGUILayout.EnumPopup(dr.Type);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("Target: ", GUILayout.Width(50));
        dr.Target = (DialogRequirementTarget)EditorGUILayout.EnumPopup(dr.Target);
        GUILayout.EndHorizontal();
        if (dr.Type != DialogRequirementType.LifeTime)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Value: ", GUILayout.Width(60));
            dr.IntValue = EditorGUILayout.IntField(dr.IntValue);
            GUILayout.EndHorizontal();
        }
        if (dr.Type == DialogRequirementType.LifeTime | dr.Type == DialogRequirementType.PastState)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Timespan: ", GUILayout.Width(60));
            dr.FloatValue = EditorGUILayout.FloatField(dr.FloatValue);
            GUILayout.EndHorizontal();
        }
        if (dr.Type == DialogRequirementType.EventLog)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Text:", GUILayout.Width(60));
            dr.StringValue = EditorGUILayout.TextField(dr.StringValue);
            GUILayout.EndHorizontal();
        }

        GUILayout.EndVertical();
        return ret;
    }

    void DisplayDialogLoop(Rect r, DialogOption dOption)
    {
        Color prev = GUI.color;
        GUI.color = Color.yellow;
        if (GUI.Button(new Rect(r.x, r.y+5, r.width, r.height-5), "Loop: " + dOption.NextDialog.Title))
        {
            InspectOptionNode(dOption);
        }
        GUI.color = prev;
        if (r.x + r.width > countedWidth) { countedWidth = r.x + r.width; }
        if (r.y + r.height > countedHeight) { countedHeight = r.y + r.height; }
    }

    void DisplaySingleDialogNode(Rect r, Dialog d, int depth, int indent)
    {
        Rect title = new Rect(r.x, r.y+5, r.width-20, r.height-5);
        if (title.y + title.height > countedHeight) { countedHeight = title.y + title.height; }
        if (title.x + title.width > countedWidth) { countedWidth = title.x + title.width; }
        string dTitle = d.Title.Description;
        if (dTitle == null || dTitle.Length == 0)
        {
            dTitle = txtNotSetMsg;
        }
        if (GUI.Button(title, dTitle))
        {
            InspectDialogNode(d);
        }
    }

    private List<Dialog> GetAllDialogsInChain(List<Dialog> dl, Dialog d)
    {
        if (!dl.Contains(d))
        {
            dl.Add(d);
        }
        for (int i = 0; i < d.Options.Count; i++)
        {
            if (d.Options[i].NextDialog != null)
            {
                if (!d.Options[i].IsRedirection)
                {
                    GetAllDialogsInChain(dl, d.Options[i].NextDialog);
                }
            }
        }
        return dl;
    }

}
