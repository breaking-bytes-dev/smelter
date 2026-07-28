"use strict";
// Webresource de los botones Ejecutar/Reintentar del form de Import Job (Fase 4.4, ver
// Assets/sanic_tbl_smlt_importjob.RibbonDiffXml.xml). Invoca prepareimport/processchunk — el
// gating de acceso (Operador solo puede sobre jobs de sus templates) lo hacen las Custom APIs
// server-side vía ExecutePrivilegeName (SecuritySpec.cs), no este script.
var Sanic = window.Sanic || {};
Sanic.Smelter = Sanic.Smelter || {};

Sanic.Smelter.PREPARE_API = "sanic_capi_smelter_prepareimport";
Sanic.Smelter.PROCESSCHUNK_API = "sanic_capi_smelter_processchunk";

Sanic.Smelter._executeGlobalApi = function (apiName, parameters) {
    var parameterTypes = {};
    Object.keys(parameters).forEach(function (key) {
        parameterTypes[key] = { typeName: "Edm.Guid", structuralProperty: 1 };
    });

    var request = {
        JobId: parameters.JobId,
        getMetadata: function () {
            return {
                boundParameter: null,
                operationType: 0,
                operationName: apiName,
                parameterTypes: parameterTypes
            };
        }
    };

    return Xrm.WebApi.online.execute(request).then(function (response) {
        return response.json ? response.json() : {};
    });
};

Sanic.Smelter._processChunksUntilDone = function (jobId) {
    return Sanic.Smelter._executeGlobalApi(Sanic.Smelter.PROCESSCHUNK_API, { JobId: jobId })
        .then(function (result) {
            if (result && result.HasMore) {
                return Sanic.Smelter._processChunksUntilDone(jobId);
            }
        });
};

Sanic.Smelter._runImportJob = function (primaryControl, jobId) {
    return Sanic.Smelter._executeGlobalApi(Sanic.Smelter.PREPARE_API, { JobId: jobId })
        .then(function () {
            return Sanic.Smelter._processChunksUntilDone(jobId);
        })
        .then(function () {
            if (primaryControl && primaryControl.refresh) { primaryControl.refresh(); }
        }, function (error) {
            Xrm.Navigation.openErrorDialog({ message: error.message });
        });
};

Sanic.Smelter._currentJobId = function (primaryControl) {
    return primaryControl.data.entity.getId().replace(/[{}]/g, "");
};

Sanic.Smelter.executeImportJob = function (primaryControl) {
    return Sanic.Smelter._runImportJob(primaryControl, Sanic.Smelter._currentJobId(primaryControl));
};

Sanic.Smelter.retryImportJob = function (primaryControl) {
    return Sanic.Smelter._runImportJob(primaryControl, Sanic.Smelter._currentJobId(primaryControl));
};
