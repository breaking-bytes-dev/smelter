"use strict";
// Webresource de los botones Ejecutar/Reintentar del form de Import Job (ver
// Assets/sanic_tbl_smlt_importjob.RibbonDiffXml.xml). Hay UN SOLO camino de ejecución: el Cloud Flow
// "Smelter orchestrator" (trigger sobre sanic_smlt_ij_status eq 0). Estos botones NO procesan nada:
// solo validan, guardan y ponen el job en Queued (0). El procesamiento corre server-side, así que
// cerrar la pestaña no afecta al job.
//
// Seguridad: el botón corre con los privilegios del usuario. Para pasar a Queued hace falta Write
// sobre el job (Dataverse lo exige nativamente) y, como la preparación server-side ya NO ve al usuario real
// (InitiatingUserId pasa a ser la identidad del flow), el botón verifica además -bajo el usuario-
// que puede leer el Import Template del job (si no, Dataverse devuelve 403 y no se encola).
var Sanic = window.Sanic || {};
Sanic.Smelter = Sanic.Smelter || {};

Sanic.Smelter.JOB_TABLE = "sanic_tbl_smlt_importjob";
Sanic.Smelter.TEMPLATE_TABLE = "sanic_tbl_smlt_importtemplate";
Sanic.Smelter.STATUS_FIELD = "sanic_smlt_ij_status";
Sanic.Smelter.Status = { Queued: 0, Processing: 1, Completed: 2, CompletedWithErrors: 3, Failed: 4 };
Sanic.Smelter.POLL_MS = 5000;

Sanic.Smelter._currentJobId = function (primaryControl) {
    return primaryControl.data.entity.getId().replace(/[{}]/g, "");
};

Sanic.Smelter._status = function (primaryControl) {
    var attr = primaryControl && primaryControl.getAttribute(Sanic.Smelter.STATUS_FIELD);
    var value = attr ? attr.getValue() : null;
    return value === undefined ? null : value;
};

// ── Enable rules (CustomRule del RibbonDiffXml) ─────────────────────────────────────────────
// Ejecutar: solo jobs que nunca corrieron (status vacío; el campo no tiene default). Queued y
// Processing quedan deshabilitados para no re-disparar el flow ni pisar un proceso en curso.
Sanic.Smelter.canExecuteImportJob = function (primaryControl) {
    return Sanic.Smelter._status(primaryControl) === null;
};

// Reintentar: solo jobs que terminaron (Completed / CompletedWithErrors / Failed).
Sanic.Smelter.canRetryImportJob = function (primaryControl) {
    var s = Sanic.Smelter._status(primaryControl);
    var St = Sanic.Smelter.Status;
    return s === St.Completed || s === St.CompletedWithErrors || s === St.Failed;
};

// ── Queueing ────────────────────────────────────────────────────────────────────────────────
Sanic.Smelter._showError = function (message) {
    return Xrm.Navigation.openErrorDialog({ message: message });
};

Sanic.Smelter._validateJob = function (jobId) {
    var query = "?$select=sanic_smlt_ij_sourcefile_name,_sanic_smlt_ij_importtemplateid_value";
    return Xrm.WebApi.retrieveRecord(Sanic.Smelter.JOB_TABLE, jobId, query).then(function (job) {
        var templateId = job["_sanic_smlt_ij_importtemplateid_value"];
        if (!templateId) {
            throw new Error("El job no tiene Import Template. Elegí uno y guardá antes de ejecutar.");
        }
        if (!job["sanic_smlt_ij_sourcefile_name"]) {
            throw new Error("El job no tiene archivo cargado. Subí el CSV en 'Source File' antes de ejecutar.");
        }
        // Chequeo de acceso al template bajo el usuario que hizo click (ver nota de seguridad arriba).
        return Xrm.WebApi.retrieveRecord(Sanic.Smelter.TEMPLATE_TABLE, templateId, "?$select=createdon")
            .then(null, function () {
                throw new Error("No tenés acceso de lectura al Import Template de este job.");
            });
    });
};

Sanic.Smelter._queueImportJob = function (primaryControl) {
    var jobId = Sanic.Smelter._currentJobId(primaryControl);
    // Guardar primero: el archivo/template/dry-run pendientes en el form tienen que estar en el server.
    return primaryControl.data.save()
        .then(function () { return Sanic.Smelter._validateJob(jobId); })
        .then(function () {
            var update = {};
            update[Sanic.Smelter.STATUS_FIELD] = Sanic.Smelter.Status.Queued;
            return Xrm.WebApi.updateRecord(Sanic.Smelter.JOB_TABLE, jobId, update);
        })
        .then(function () {
            primaryControl.data.refresh(false);
            Xrm.App.addGlobalNotification({
                type: 2,
                level: 1, // success
                message: "Import en cola: el procesamiento corre en el servidor; podés cerrar esta ventana.",
                showCloseButton: true
            });
            Sanic.Smelter._watch(primaryControl, jobId);
        })
        .then(null, function (error) {
            return Sanic.Smelter._showError((error && error.message) || String(error));
        });
};

// Auto-refresh liviano: mientras el job está Queued/Processing refresca el form cada POLL_MS y se
// corta solo en estado terminal, o si el form ya no existe (navegación/cierre => la llamada tira).
Sanic.Smelter._watch = function (primaryControl, jobId) {
    var St = Sanic.Smelter.Status;
    var tick = function () {
        var current;
        try { current = Sanic.Smelter._currentJobId(primaryControl); } catch (e) { return; }
        if (current !== jobId) { return; }
        Xrm.WebApi.retrieveRecord(Sanic.Smelter.JOB_TABLE, jobId, "?$select=" + Sanic.Smelter.STATUS_FIELD)
            .then(function (job) {
                var s = job[Sanic.Smelter.STATUS_FIELD];
                try { primaryControl.data.refresh(false); } catch (e) { return; }
                if (s === St.Queued || s === St.Processing) { setTimeout(tick, Sanic.Smelter.POLL_MS); }
            }, function () { /* error de red/form cerrado: dejar de pollear */ });
    };
    setTimeout(tick, Sanic.Smelter.POLL_MS);
};

Sanic.Smelter.executeImportJob = function (primaryControl) {
    return Sanic.Smelter._queueImportJob(primaryControl);
};

// Reintentar vuelve a poner Queued: el flow dispara de nuevo y la preparación (Prepare) resetea contadores,
// offset y cursor de bytes, así que la corrida arranca desde cero.
Sanic.Smelter.retryImportJob = function (primaryControl) {
    return Sanic.Smelter._queueImportJob(primaryControl);
};
