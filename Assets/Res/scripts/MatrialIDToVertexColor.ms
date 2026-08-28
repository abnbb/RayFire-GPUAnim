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

    fn processObj obj =
    (
        if not (canConvertTo obj Editable_Poly) then
        (
            format "Skip: % cannot convert to Editable Poly\n" obj.name
            false
        )
        else
        (
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

                -- ??????????
                polyop.setFaceSmoothGroup obj #{f} sgMask

                -- ??????????,???? ID ????????
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

            -- ??????
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

    if selection.count == 0 then
    (
        messageBox "Please select object first."
    )
    else
    (
        local count = 0

        for obj in selection do
        (
            if processObj obj do count += 1
        )

        format "Done. Processed % object(s).\n" count
    )
)