(
    /*
        RayFire inner/outer face workflow for 3ds Max 2022.

        Step 1:
        - Record all existing Material IDs and smoothing groups.
        - Open RayFire. Add the current selection to Dynamic Objects manually.

        Step 2:
        - After fracturing, select all resulting fragments.
        - Find Material IDs that did not exist in Step 1.
        - Select those faces for visual confirmation.

        Step 3:
        - After confirmation, assign the inner faces a free smoothing group.
        - Write outer faces as black and inner faces as red in Vertex Color R.
        - Optionally move/rotate the pivot only.
    */

    local vertexColorChannel = 0
    local faceSubObjectLevel = 4

    global MatIDVC_RF_OriginalMatIDs = #()
    global MatIDVC_RF_OriginalSmoothGroups = #()
    global MatIDVC_RF_OriginalMatSGPairs = #()
    global MatIDVC_RF_OriginalObjectSummaries = #()
    global MatIDVC_RF_SourceNames = #()
    global MatIDVC_RF_AnalysisNodes = #()
    global MatIDVC_RF_InnerFaceSets = #()
    global MatIDVC_RF_NewMatIDs = #()
    global MatIDVC_RF_Prepared = false
    global MatIDVC_RF_Analyzed = false

    fn getAxisVector axisIndex =
    (
        case axisIndex of
        (
            1: [1,0,0]
            2: [0,1,0]
            3: [0,0,1]
            default: [1,0,0]
        )
    )

    fn rotatePivotOnly obj axisIndex angleDeg =
    (
        local axisVector = getAxisVector axisIndex
        local rotVal = angleaxis angleDeg axisVector
        local rotValInv = inverse (rotVal as quat)

        animate off
        (
            in coordsys local obj.rotation *= rotValInv
            obj.objectOffsetPos *= rotValInv
            obj.objectOffsetRot *= rotValInv
        )
    )

    fn applyPivotOptions obj setPivot pivotPos rotatePivot axisIndex angleDeg =
    (
        with undo off
        (
            if setPivot do
                in coordsys world obj.pivot = pivotPos

            if rotatePivot do
                rotatePivotOnly obj axisIndex angleDeg
        )
    )

    fn getSelectedPolyObjects =
    (
        local nodes = #()

        for obj in (selection as array) do
        (
            if canConvertTo obj Editable_Poly do
                append nodes obj
        )

        nodes
    )

    fn addUniqueValue values value =
    (
        if findItem values value == 0 do
            append values value

        values
    )

    fn getSmoothMaskTable =
    (
        #(
            1, 2, 4, 8, 16, 32, 64, 128,
            256, 512, 1024, 2048, 4096, 8192, 16384, 32768,
            65536, 131072, 262144, 524288, 1048576, 2097152,
            4194304, 8388608, 16777216, 33554432, 67108864,
            134217728, 268435456, 536870912, 1073741824,
            -2147483648
        )
    )

    fn addSmoothGroupsFromMask targetGroups sgMask =
    (
        local smoothMasks = getSmoothMaskTable()

        if sgMask == 0 then
        (
            targetGroups = addUniqueValue targetGroups 0
        )
        else
        (
            for i = 1 to smoothMasks.count do
            (
                if (bit.and sgMask smoothMasks[i]) != 0 do
                    targetGroups = addUniqueValue targetGroups i
            )
        )

        targetGroups
    )

    fn getSmoothGroupListText groups =
    (
        local text = ""

        for i = 1 to groups.count do
        (
            local label = if groups[i] == 0 then "None" else (groups[i] as string)
            text += label

            if i < groups.count do
                text += ", "
        )

        text
    )

    fn showOriginalStateMessage =
    (
        local matText = MatIDVC_RF_OriginalMatIDs as string
        local sgText = getSmoothGroupListText MatIDVC_RF_OriginalSmoothGroups
        local objectText = ""

        for i = 1 to MatIDVC_RF_OriginalObjectSummaries.count do
        (
            objectText += MatIDVC_RF_OriginalObjectSummaries[i]

            if i < MatIDVC_RF_OriginalObjectSummaries.count do
                objectText += "\n"
        )

        messageBox (
            "Recorded source objects: " + (MatIDVC_RF_SourceNames.count as string) + "\n" +
            "Material ID count: " + (MatIDVC_RF_OriginalMatIDs.count as string) + "\n" +
            "Material IDs: " + matText + "\n\n" +
            "Smoothing group count: " + (MatIDVC_RF_OriginalSmoothGroups.count as string) + "\n" +
            "Smoothing groups: " + sgText + "\n\n" +
            "Per-object summary:\n" + objectText
        ) title:"Original Material IDs / Smoothing Groups"
    )

    fn captureOriginalState nodes =
    (
        MatIDVC_RF_OriginalMatIDs = #()
        MatIDVC_RF_OriginalSmoothGroups = #()
        MatIDVC_RF_OriginalMatSGPairs = #()
        MatIDVC_RF_OriginalObjectSummaries = #()
        MatIDVC_RF_SourceNames = #()

        for obj in nodes do
        (
            convertToPoly obj
            append MatIDVC_RF_SourceNames obj.name

            local faceCount = polyop.getNumFaces obj
            local objectMatIDs = #()
            local objectSmoothGroups = #()

            for f = 1 to faceCount do
            (
                local matID = polyop.getFaceMatID obj f
                local sgMask = polyop.getFaceSmoothGroup obj f

                MatIDVC_RF_OriginalMatIDs = addUniqueValue MatIDVC_RF_OriginalMatIDs matID
                MatIDVC_RF_OriginalSmoothGroups = addSmoothGroupsFromMask MatIDVC_RF_OriginalSmoothGroups sgMask
                objectMatIDs = addUniqueValue objectMatIDs matID
                objectSmoothGroups = addSmoothGroupsFromMask objectSmoothGroups sgMask

                local pairFound = false
                for pair in MatIDVC_RF_OriginalMatSGPairs do
                (
                    if pair[1] == matID and pair[2] == sgMask do
                        pairFound = true
                )

                if not pairFound do
                    append MatIDVC_RF_OriginalMatSGPairs #(matID, sgMask)
            )

            sort objectMatIDs
            sort objectSmoothGroups

            append MatIDVC_RF_OriginalObjectSummaries (
                obj.name +
                ": Material IDs " + (objectMatIDs as string) +
                " | Smoothing Groups " + (getSmoothGroupListText objectSmoothGroups)
            )
        )

        sort MatIDVC_RF_OriginalMatIDs
        sort MatIDVC_RF_OriginalSmoothGroups
        true
    )

    fn openRayFirePanel =
    (
        local opened = false

        try
        (
            if not TabRolRf.Open do
                RayFireSt.Launch()

            opened = true
        )
        catch
        (
            try
            (
                macros.run "RayFire Tool" "RayFireLauncherRF"
                opened = true
            )
            catch
            (
                opened = false
            )
        )

        opened
    )

    fn clearWorkflowState =
    (
        MatIDVC_RF_OriginalMatIDs = #()
        MatIDVC_RF_OriginalSmoothGroups = #()
        MatIDVC_RF_OriginalMatSGPairs = #()
        MatIDVC_RF_OriginalObjectSummaries = #()
        MatIDVC_RF_SourceNames = #()
        MatIDVC_RF_AnalysisNodes = #()
        MatIDVC_RF_InnerFaceSets = #()
        MatIDVC_RF_NewMatIDs = #()
        MatIDVC_RF_Prepared = false
        MatIDVC_RF_Analyzed = false
    )

    fn analyzeCurrentSelection =
    (
        MatIDVC_RF_AnalysisNodes = #()
        MatIDVC_RF_InnerFaceSets = #()
        MatIDVC_RF_NewMatIDs = #()
        MatIDVC_RF_Analyzed = false

        for obj in (selection as array) do
        (
            if canConvertTo obj Editable_Poly do
            (
                convertToPoly obj
                append MatIDVC_RF_AnalysisNodes obj

                local faceCount = polyop.getNumFaces obj
                local innerFaces = #{}

                for f = 1 to faceCount do
                (
                    local matID = polyop.getFaceMatID obj f

                    if findItem MatIDVC_RF_OriginalMatIDs matID == 0 do
                    (
                        innerFaces[f] = true
                        MatIDVC_RF_NewMatIDs = addUniqueValue MatIDVC_RF_NewMatIDs matID
                    )
                )

                if innerFaces.numberset > 0 do
                    append MatIDVC_RF_InnerFaceSets #(obj, innerFaces)
            )
        )

        sort MatIDVC_RF_NewMatIDs

        if MatIDVC_RF_InnerFaceSets.count > 0 then
        (
            clearSelection()
            select MatIDVC_RF_AnalysisNodes
            max modify mode
            subObjectLevel = faceSubObjectLevel

            for item in MatIDVC_RF_InnerFaceSets do
                polyop.setFaceSelection item[1] item[2]

            true
        )
        else
        (
            false
        )
    )

    fn getFreeSmoothGroup obj innerFaces =
    (
        local smoothMasks = #(
            1, 2, 4, 8, 16, 32, 64, 128,
            256, 512, 1024, 2048, 4096, 8192, 16384, 32768,
            65536, 131072, 262144, 524288, 1048576, 2097152,
            4194304, 8388608, 16777216, 33554432, 67108864,
            134217728, 268435456, 536870912, 1073741824,
            -2147483648
        )

        for mask in smoothMasks do
        (
            local occupied = false
            local faceCount = polyop.getNumFaces obj

            for f = 1 to faceCount do
            (
                if not innerFaces[f] do
                (
                    local currentMask = polyop.getFaceSmoothGroup obj f

                    if (bit.and currentMask mask) != 0 do
                        occupied = true
                )
            )

            if not occupied do
                return mask
        )

        0
    )

    fn writeInnerOuterVertexColors obj innerFaces =
    (
        polyop.setMapSupport obj vertexColorChannel true

        local faceCount = polyop.getNumFaces obj
        local totalMapVerts = 0

        for f = 1 to faceCount do
            totalMapVerts += polyop.getFaceDeg obj f

        polyop.setNumMapVerts obj vertexColorChannel totalMapVerts

        local mapVertIndex = 1

        for f = 1 to faceCount do
        (
            local faceDegree = polyop.getFaceDeg obj f
            local mapFace = #()
            local faceColor = color 0 0 0

            if innerFaces[f] do
                faceColor = color 255 0 0

            for i = 1 to faceDegree do
            (
                polyop.setMapVert obj vertexColorChannel mapVertIndex faceColor
                append mapFace mapVertIndex
                mapVertIndex += 1
            )

            polyop.setMapFace obj vertexColorChannel f mapFace
        )

        try(obj.showVertexColors = true) catch()
        try(obj.vertexColorType = 0) catch()
        update obj
    )

    fn setFBXExportParamSafe paramName paramValue =
    (
        try
        (
            FBXExporterSetParam paramName paramValue
            true
        )
        catch
        (
            format "FBX parameter unavailable: % = %\n" paramName paramValue
            false
        )
    )

    fn configureFBXExport bakeAnimation startFrame endFrame frameStep =
    (
        -- These parameters are supported by the FBX exporter shipped with Max 2022.
        setFBXExportParamSafe "Animation" bakeAnimation
        setFBXExportParamSafe "BakeAnimation" bakeAnimation
        setFBXExportParamSafe "BakeFrameStart" startFrame
        setFBXExportParamSafe "BakeFrameEnd" endFrame
        setFBXExportParamSafe "BakeFrameStep" frameStep
        setFBXExportParamSafe "BakeResampleAnimation" bakeAnimation

        setFBXExportParamSafe "SmoothingGroups" true
        setFBXExportParamSafe "VertexColor" true
        setFBXExportParamSafe "Normals" true
        setFBXExportParamSafe "Triangulate" false
        setFBXExportParamSafe "Cameras" false
        setFBXExportParamSafe "Lights" false
        true
    )

    fn exportSelectedObjectsToFBX bakeAnimation startFrame endFrame frameStep =
    (
        if selection.count == 0 then
        (
            messageBox "Select the processed objects to export first." title:"FBX Export"
            false
        )
        else
        (
            local filePath = getSaveFileName caption:"Export Selected Objects to FBX" types:"FBX (*.fbx)|*.fbx|" filename:""

            if filePath == undefined then
            (
                false
            )
            else
            (
                if (toLower (getFilenameType filePath)) != ".fbx" do
                    filePath += ".fbx"

                configureFBXExport bakeAnimation startFrame endFrame frameStep

                local exported = false
                try
                (
                    exportFile filePath #noPrompt selectedOnly:true
                    exported = true
                )
                catch
                (
                    messageBox ("FBX export failed:\n" + (getCurrentException() as string)) title:"FBX Export"
                )

                if exported then
                (
                    messageBox (
                        "Export completed.\n\n" +
                        "File: " + filePath + "\n" +
                        "Objects: " + (selection.count as string) + "\n" +
                        "Smoothing groups, vertex colors and normals were enabled.\n" +
                        "Animation baking: " + (if bakeAnimation then "On" else "Off")
                    ) title:"FBX Export"
                )

                exported
            )
        )
    )

    fn applyInnerOuterClassification setPivot pivotPos rotatePivot axisIndex angleDeg =
    (
        local appliedCount = 0
        local noFreeGroupCount = 0

        for item in MatIDVC_RF_InnerFaceSets do
        (
            local obj = item[1]
            local innerFaces = item[2]

            local newSmoothGroup = getFreeSmoothGroup obj innerFaces

            if newSmoothGroup != 0 then
            (
                polyop.setFaceSmoothGroup obj innerFaces newSmoothGroup
                writeInnerOuterVertexColors obj innerFaces
                format "%: inner faces %, new smoothing group %\n" obj.name innerFaces.numberset newSmoothGroup
                appliedCount += 1
            )
            else
            (
                format "%: no free smoothing group available\n" obj.name
                noFreeGroupCount += 1
            )
        )

        for obj in MatIDVC_RF_AnalysisNodes do
            applyPivotOptions obj setPivot pivotPos rotatePivot axisIndex angleDeg

        clearSelection()
        select MatIDVC_RF_AnalysisNodes
        subObjectLevel = 0

        #(appliedCount, noFreeGroupCount)
    )

    rollout MatIDVC_RayFire_Rollout "RayFire Inner Outer Workflow"
    (
        groupBox grpPivot "Final Pivot Setup" pos:[8,8] width:344 height:92
        checkbox chkSetPivot "Set pivot position" pos:[20,30] checked:true
        spinner spnPivotX "X" pos:[28,54] width:78 type:#float range:[-999999,999999,0]
        spinner spnPivotY "Y" pos:[112,54] width:78 type:#float range:[-999999,999999,0]
        spinner spnPivotZ "Z" pos:[196,54] width:78 type:#float range:[-999999,999999,0]

        groupBox grpPivotRot "Pivot Rotation" pos:[8,108] width:344 height:70
        checkbox chkRotatePivot "Rotate pivot" pos:[20,130] checked:true
        dropdownList ddlAxis "Axis" pos:[102,128] width:70 items:#("X", "Y", "Z") selection:1
        spinner spnAngle "Deg" pos:[200,128] width:110 type:#float range:[-3600,3600,90]

        groupBox grpWorkflow "Confirmed Workflow" pos:[8,188] width:344 height:168
        button btnPrepare "1. Record IDs + Open RayFire" pos:[20,210] width:320 height:30
        button btnAnalyze "2. Scan Selection + Select Inner Faces" pos:[20,248] width:320 height:30 enabled:false
        button btnApply "3. Confirm Selection + Apply" pos:[20,286] width:320 height:30 enabled:false
        button btnReset "Reset Workflow" pos:[20,324] width:150 height:24
        button btnOpen "Open RayFire" pos:[190,324] width:150 height:24

        groupBox grpExport "FBX Export" pos:[8,366] width:344 height:126
        checkbox chkBakeAnimation "Bake animation" pos:[20,388] checked:true
        label lblStart "Start" pos:[20,420] width:35
        spinner spnBakeStart pos:[54,418] width:72 type:#float range:[-1000000,1000000,animationRange.start]
        label lblEnd "End" pos:[136,420] width:28
        spinner spnBakeEnd pos:[166,418] width:72 type:#float range:[-1000000,1000000,animationRange.end]
        label lblStep "Step" pos:[254,420] width:28
        spinner spnBakeStep pos:[286,418] width:52 type:#float range:[0.001,1000,1]
        button btnExport "4. Export Selected Objects to FBX" pos:[20,456] width:320 height:28 enabled:false

        label lblStatus "Status: Select source objects." pos:[12,504] width:338 height:34
        label lblInfo "Pivot options are applied in Step 3 after confirmation." pos:[12,544] width:338 height:42

        on btnPrepare pressed do
        (
            local nodes = getSelectedPolyObjects()

            if nodes.count == 0 then
            (
                messageBox "Select the original objects first."
            )
            else
            (
                captureOriginalState nodes
                MatIDVC_RF_Prepared = true
                MatIDVC_RF_Analyzed = false

                btnAnalyze.enabled = true
                btnApply.enabled = false
                btnExport.enabled = false

                local rayFireOpened = openRayFirePanel()
                local openText = if rayFireOpened then "RayFire opened." else "RayFire could not be opened."

                lblStatus.text = ("Status: Recorded " + (MatIDVC_RF_OriginalMatIDs.count as string) + " original Material IDs.")
                lblInfo.text = (openText + " Add to Dynamic Objects, fracture, then select fragments for Step 2.")

                format "Original Material IDs: %\n" MatIDVC_RF_OriginalMatIDs
                format "Original Smoothing Groups: %\n" MatIDVC_RF_OriginalSmoothGroups
                format "Original Material ID / Smoothing Group pairs: %\n" MatIDVC_RF_OriginalMatSGPairs

                showOriginalStateMessage()
            )
        )

        on btnAnalyze pressed do
        (
            if not MatIDVC_RF_Prepared then
            (
                messageBox "Run Step 1 first."
            )
            else if selection.count == 0 then
            (
                messageBox "After fracturing, select all resulting fragments first."
            )
            else
            (
                local foundInner = analyzeCurrentSelection()

                if foundInner then
                (
                    MatIDVC_RF_Analyzed = true
                    btnApply.enabled = true

                    local innerFaceCount = 0
                    for item in MatIDVC_RF_InnerFaceSets do
                        innerFaceCount += item[2].numberset

                    lblStatus.text = ("Status: Selected " + (innerFaceCount as string) + " inner faces.")
                    lblInfo.text = ("New Material IDs: " + (MatIDVC_RF_NewMatIDs as string) + ". Inspect the selected red-target faces, then click Step 3.")

                    format "New Material IDs treated as inner: %\n" MatIDVC_RF_NewMatIDs
                    format "Inner face count: %\n" innerFaceCount
                )
                else
                (
                    btnApply.enabled = false
                    lblStatus.text = "Status: No new Material ID was found."
                    lblInfo.text = "Select the resulting fragments and run the scan again."
                    messageBox "No new Material ID was found in the current selection."
                )
            )
        )

        on btnApply pressed do
        (
            if not MatIDVC_RF_Analyzed then
            (
                messageBox "Run Step 2 first and inspect the selected inner faces."
            )
            else
            (
                local answer = queryBox "The selected new Material ID faces will be assigned a new smoothing group and colored red. Continue?" title:"Confirm Inner Faces"

                if answer then
                (
                    local pivotPos = [spnPivotX.value, spnPivotY.value, spnPivotZ.value]
                    local result = applyInnerOuterClassification chkSetPivot.checked pivotPos chkRotatePivot.checked ddlAxis.selection spnAngle.value

                    lblStatus.text = ("Status: Applied to " + (result[1] as string) + " object(s).")
                    lblInfo.text = "Outer faces are black; inner faces are red. Pivot setup was applied last."
                    btnApply.enabled = false
                    btnExport.enabled = (result[1] > 0)
                )
            )
        )

        on btnReset pressed do
        (
            clearWorkflowState()
            btnAnalyze.enabled = false
            btnApply.enabled = false
            btnExport.enabled = false
            lblStatus.text = "Status: Select source objects."
            lblInfo.text = "Pivot options are applied in Step 3 after confirmation."
        )

        on btnExport pressed do
        (
            if not MatIDVC_RF_Analyzed and not MatIDVC_RF_Prepared then
            (
                messageBox "Complete Steps 1 to 3 before exporting." title:"FBX Export"
            )
            else
            (
                local bakeStart = spnBakeStart.value
                local bakeEnd = spnBakeEnd.value
                local bakeStep = spnBakeStep.value

                if bakeEnd < bakeStart then
                (
                    messageBox "End frame must be greater than or equal to Start frame." title:"FBX Export"
                )
                else if bakeStep <= 0 then
                (
                    messageBox "Bake step must be greater than zero." title:"FBX Export"
                )
                else
                (
                    local bakeNotice = if chkBakeAnimation.checked then
                        "\n\nMake sure the RayFire animation has already been baked."
                    else
                        ""

                    local answer = queryBox (
                        "Export the current selection as FBX?" + bakeNotice +
                        "\n\nSelected objects: " + (selection.count as string)
                    ) title:"Confirm FBX Export"

                    if answer do
                    (
                        if exportSelectedObjectsToFBX chkBakeAnimation.checked bakeStart bakeEnd bakeStep then
                            lblInfo.text = "FBX export completed with smoothing groups and vertex colors."
                    )
                )
            )
        )

        on btnOpen pressed do
        (
            if openRayFirePanel() then
                lblInfo.text = "RayFire opened. Use Dynamic Objects > Add with the intended selection."
            else
                lblInfo.text = "RayFire could not be opened. Run RayFireLauncherRF manually."
        )
    )

    try(destroyDialog MatIDVC_RayFire_Rollout) catch()
    clearWorkflowState()
    createDialog MatIDVC_RayFire_Rollout 360 600
)
