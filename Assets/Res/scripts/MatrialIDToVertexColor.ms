(
    local vertexColorChannel = 0 -- Vertex Color

    fn getSmoothGroupByMatID matID =
    (
        if matID == 1 then
            1
        else if matID == 2 then
            2
        else if ((matID / 2) as integer) * 2 == matID then
            2
        else
            1
    )

    fn getVertexColorByMatID matID =
    (
        if matID == 1 then
            color 0 0 0
        else
            color 255 0 0
    )

    fn getAxisVector axisIndex =
    (
        case axisIndex of
        (
            1: [1,0,0] -- X
            2: [0,1,0] -- Y
            3: [0,0,1] -- Z
            default: [1,0,0]
        )
    )

    fn rotatePivotOnly obj axisVec angleDeg =
    (
        local rotVal = angleaxis angleDeg axisVec
        local rotValInv = inverse (rotVal as quat)

        animate off
        (
            in coordsys local obj.rotation *= rotValInv
            obj.objectOffsetPos *= rotValInv
            obj.objectOffsetRot *= rotValInv
        )
    )

    fn applyPivotOptions obj doSetPivot pivotPos doRotatePivot axisIndex angleDeg =
    (
        with undo off
        (
            if doSetPivot do
            (
                in coordsys world obj.pivot = pivotPos
            )

            if doRotatePivot do
            (
                local axisVec = getAxisVector axisIndex
                rotatePivotOnly obj axisVec angleDeg
            )
        )
    )

    fn processObj obj doSetPivot pivotPos doRotatePivot axisIndex angleDeg =
    (
        if not (canConvertTo obj Editable_Poly) then
        (
            format "Skip: % cannot convert to Editable Poly\n" obj.name
            false
        )
        else
        (
            applyPivotOptions obj doSetPivot pivotPos doRotatePivot axisIndex angleDeg

            convertToPoly obj

            local faceCount = polyop.getNumFaces obj
            polyop.setMapSupport obj vertexColorChannel true

            local totalMapVerts = 0
            for f = 1 to faceCount do
            (
                totalMapVerts += polyop.getFaceDeg obj f
            )

            polyop.setNumMapVerts obj vertexColorChannel totalMapVerts

            local mapVertIndex = 1

            for f = 1 to faceCount do
            (
                local matID = polyop.getFaceMatID obj f
                local sgMask = getSmoothGroupByMatID matID
                local vc = getVertexColorByMatID matID
                local faceDegree = polyop.getFaceDeg obj f
                local mapFace = #()

                polyop.setFaceSmoothGroup obj #{f} sgMask

                for i = 1 to faceDegree do
                (
                    polyop.setMapVert obj vertexColorChannel mapVertIndex vc
                    append mapFace mapVertIndex
                    mapVertIndex += 1
                )

                polyop.setMapFace obj vertexColorChannel f mapFace
            )

            try(obj.showVertexColors = true) catch()
            try(obj.vertexColorType = 0) catch()

            update obj

            local sg1Count = 0
            local sg2Count = 0

            for f = 1 to faceCount do
            (
                local sg = polyop.getFaceSmoothGroup obj f

                if sg == 1 then sg1Count += 1
                if sg == 2 then sg2Count += 1
            )

            format "% result: SG1 faces %, SG2 faces %\n" obj.name sg1Count sg2Count

            true
        )
    )

    rollout MatIDToVC_Rollout "MatID To Vertex Color"
    (
        groupBox grpPivotPos "Pivot Position" pos:[8,8] width:264 height:88

        checkbox chkSetPivot "Set pivot position" pos:[20,30] checked:true

        spinner spnPivotX "X" pos:[28,54] width:74 type:#float range:[-999999,999999,0]
        spinner spnPivotY "Y" pos:[108,54] width:74 type:#float range:[-999999,999999,0]
        spinner spnPivotZ "Z" pos:[188,54] width:74 type:#float range:[-999999,999999,0]

        groupBox grpPivotRot "Pivot Rotation" pos:[8,104] width:264 height:82

        checkbox chkRotatePivot "Rotate pivot" pos:[20,126] checked:true

        dropdownList ddlAxis "Axis" pos:[28,148] width:74 items:#("X", "Y", "Z") selection:1
        spinner spnAngle "Deg" pos:[126,150] width:118 type:#float range:[-3600,3600,90]

        button btnRun "Process Selected" pos:[8,198] width:264 height:30

        on btnRun pressed do
        (
            if selection.count == 0 then
            (
                messageBox "Please select object first."
            )
            else
            (
                local count = 0
                local pivotPos = [spnPivotX.value, spnPivotY.value, spnPivotZ.value]

                for obj in selection do
                (
                    if processObj obj chkSetPivot.checked pivotPos chkRotatePivot.checked ddlAxis.selection spnAngle.value do
                    (
                        count += 1
                    )
                )

                format "Done. Processed % object(s).\n" count
                destroyDialog MatIDToVC_Rollout
            )
        )
    )

    try(destroyDialog MatIDToVC_Rollout) catch()
    createDialog MatIDToVC_Rollout 280 240
)