// Ejecuta los casos de validación del informe y deja el resultado esperado y el obtenido en un JSON.
// Uso: dotnet run --project tools/Enami.Evidencias -- "<carpeta de las planillas>" evidencias.json
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Enami.Dominio.Calculo;
using Enami.Dominio.Calculo.Expresiones;
using Enami.Dominio.Modelo;
using Enami.Dominio.Persistencia;
using Enami.Dominio.Servicios;
using Enami.Pruebas;

var es = CultureInfo.GetCultureInfo("es-CL");
string N(double x, int d) => x.ToString("N" + d, es);
var casos = new List<Dictionary<string, string>>();
void Caso(string id, string grupo, string req, string caso, string entrada, string esperado, string obtenido, bool ok, string prueba) =>
    casos.Add(new() { ["id"] = id, ["grupo"] = grupo, ["req"] = req, ["caso"] = caso, ["entrada"] = entrada, ["esperado"] = esperado, ["obtenido"] = obtenido, ["resultado"] = ok ? "Aprobado" : "Fallido", ["prueba"] = prueba });
string Error(Action a) { try { a(); return "(no hubo error)"; } catch (Exception ex) { return ex.Message; } }

// ---------- 1. Cálculo financiero: Anexo 2 contra la planilla de ENAMI ----------
var r = MotorEvaluacion.Evaluar(PlantillaAnexo2.Crear());
const string fuente = "Planilla ENAMI «Ev.Proyectos_Reconocimiento_Minerales_enero_2026_ejemplo.xlsx», hoja «anexos 2 y 4»";
var esc1 = r.Escenarios[0].ValoresFormulas; var proy = r.Escenarios[^1].ValoresFormulas;
Caso("CF-01", "Cálculo financiero", "RF-02", "Ingreso por tonelada al precio de la primera fila (3.145 US$/oz)", fuente, "579,245 US$/t (celda T31)", N(esc1["Ingreso"], 3) + " US$/t", Math.Abs(esc1["Ingreso"] - 579.2447) < 0.001, "El_Anexo_2_reproduce_la_planilla_de_ENAMI");
Caso("CF-02", "Cálculo financiero", "RF-02", "Capacidad de pago de la primera fila", fuente, "5.161.903,91 US$ (F34)", N(esc1["CapacidadPago"], 2) + " US$", Math.Abs(esc1["CapacidadPago"] - 5161903.914) < 0.01, "El_Anexo_2_reproduce_la_planilla_de_ENAMI");
Caso("CF-03", "Cálculo financiero", "RF-02", "Ingreso al precio proyectado (3.224,5 US$/oz)", fuente, "593,576 US$/t (B42)", N(proy["Ingreso"], 3) + " US$/t", Math.Abs(proy["Ingreso"] - 593.5758) < 0.001, "El_Anexo_2_reproduce_la_planilla_de_ENAMI");
Caso("CF-04", "Cálculo financiero", "RF-02", "Recuperación esperada del fomento", fuente, "161.450 US$ (H42)", N(r.Recuperacion, 0) + " US$", Math.Abs(r.Recuperacion - 161450) < 1e-6, "El_Anexo_2_reproduce_la_planilla_de_ENAMI");
Caso("CF-05", "Cálculo financiero", "RF-02", "VAN del proyecto", fuente, "7.683.901,91 US$ (I42)", N(r.Van, 2) + " US$", Math.Abs(r.Van - 7683901.91) < 0.1, "El_Anexo_2_reproduce_la_planilla_de_ENAMI");
Caso("CF-06", "Cálculo financiero", "RF-02", "Precio crítico operacional (utilidad = 0)", fuente, "372,07 US$/oz (D28)", N(r.PrecioCritico!.Value, 3) + " US$/oz", Math.Abs(r.PrecioCritico.Value - 372.069) < 0.01, "El_Anexo_2_reproduce_la_planilla_de_ENAMI");
Caso("CF-07", "Cálculo financiero", "RF-02", "Precio mínimo de retorno (capacidad de pago = financiamiento)", fuente, "488,18 US$/oz (D29)", N(r.PrecioMinimoRetorno!.Value, 3) + " US$/oz", Math.Abs(r.PrecioMinimoRetorno.Value - 488.178) < 0.01, "El_Anexo_2_reproduce_la_planilla_de_ENAMI");
Caso("CF-08", "Cálculo financiero", "RF-03", "Dictamen del caso de ejemplo", fuente + "; regla: viable si la recuperación cubre el financiamiento y el VAN ≥ 0", "VIABLE", r.Viable ? "VIABLE" : "NO VIABLE", r.Viable, "El_Anexo_2_reproduce_la_planilla_de_ENAMI");
var rNo = MotorEvaluacion.Evaluar(PlantillaAnexo2.Crear(), new Dictionary<string, double> { ["RecursosIndicados"] = 300, ["ProduccionMensual"] = 20 });
Caso("CF-09", "Cálculo financiero", "RF-03", "Dictamen con recursos insuficientes", "Mismo caso con 300 t de recursos indicados y 20 t/mes de producción", "NO VIABLE (recuperación < financiamiento)", $"{(rNo.Viable ? "VIABLE" : "NO VIABLE")}; recuperación {N(rNo.Recuperacion, 0)} US$ de {N(rNo.Inversion, 0)} US$", !rNo.Viable && rNo.Recuperacion < rNo.Inversion, "Sin_capacidad_de_pago_suficiente_no_es_viable");

// presupuesto del Anexo 1
var e0 = new Entorno();
var lab = e0.Solicitudes.Labores();
short Id(string nombre) => lab.Single(l => l.Nombre == nombre).Id;
var lineas = new List<DatosLineaPresupuesto>
{
    new() { IdLabor = Id("Sondajes"), Programa = ProgramasPresupuesto.Reconocimiento, Cantidad = 1000, CostoUnitario = 131, PorcentajeFinanciamiento = 1 },
    new() { IdLabor = Id("Análisis químico"), Programa = ProgramasPresupuesto.Reconocimiento, Cantidad = 25, CostoUnitario = 36, PorcentajeFinanciamiento = 1 },
    new() { IdLabor = Id("Gastos notariales y legales"), Programa = ProgramasPresupuesto.Propio, Cantidad = 1000, CostoUnitario = 1, PorcentajeFinanciamiento = 0 },
    new() { IdLabor = Id("Galerías 2,5 x 2,5"), Programa = ProgramasPresupuesto.Desarrollo, Cantidad = 20, CostoUnitario = 590, PorcentajeFinanciamiento = 0.5m },
    new() { IdLabor = Id("Rampas 4,0 x 4,0"), Programa = ProgramasPresupuesto.Desarrollo, Cantidad = 50, CostoUnitario = 945, PorcentajeFinanciamiento = 0.5m },
};
var t = CalculoPresupuesto.Calcular(lineas, 0.8m);
const string fuenteA1 = "Planilla ENAMI, hoja «anexo 1 y 3»: 5 actividades (sondajes, análisis, gastos legales, galerías y rampas)";
Caso("CF-10", "Cálculo financiero", "RF-02", "Presupuesto: capital de riesgo", fuenteA1, "131.900 US$ (E45)", N((double)t.CapitalRiesgo, 0) + " US$", t.CapitalRiesgo == 131_900m, "El_presupuesto_reproduce_el_Anexo_1_de_la_planilla");
Caso("CF-11", "Cálculo financiero", "RF-02", "Presupuesto: crédito para el desarrollo", fuenteA1, "29.550 US$ (F45)", N((double)t.Credito, 0) + " US$", t.Credito == 29_550m, "El_presupuesto_reproduce_el_Anexo_1_de_la_planilla");
Caso("CF-12", "Cálculo financiero", "RF-02", "Presupuesto: aporte del beneficiario", fuenteA1, "31.614 US$ (G45)", N((double)t.AportePropio, 0) + " US$", t.AportePropio == 31_614m, "El_presupuesto_reproduce_el_Anexo_1_de_la_planilla");
Caso("CF-13", "Cálculo financiero", "RF-02", "Impuesto de timbres y estampillas (0,8 %)", fuenteA1, "1.064 US$ (G31)", N((double)t.Timbres, 0) + " US$", t.Timbres == 1_064m, "El_presupuesto_reproduce_el_Anexo_1_de_la_planilla");
Caso("CF-14", "Cálculo financiero", "RF-02", "Inversión total del proyecto", fuenteA1, "193.064 US$ (B46)", N((double)t.InversionTotal, 0) + " US$", t.InversionTotal == 193_064m, "El_presupuesto_reproduce_el_Anexo_1_de_la_planilla");

// funciones financieras y VAN por flujo
var pago = Funciones.Pago(0.01, 12, -1000); var va = Funciones.Va(0.05 / 12, 60, -500);
Caso("CF-15", "Cálculo financiero", "RF-02", "Función PAGO frente a Excel", "PAGO(1 %; 12; −1.000)", "88,8488 (Excel)", N(pago, 4), Math.Abs(pago - 88.8488) < 1e-4, "VA_y_PAGO_dan_lo_mismo_que_Excel");
Caso("CF-16", "Cálculo financiero", "RF-02", "Función VA frente a Excel", "VA(5 %/12; 60; −500)", "26.495,35 (Excel)", N(va, 2), Math.Abs(va - 26495.35) < 0.01, "VA_y_PAGO_dan_lo_mismo_que_Excel");
double vanManual = -250 + 100 / 1.1 + 100 / 1.21 + 100 / 1.331;
var simple = new PlantillaCalculo
{
    Instrumento = "Prueba", Version = 1,
    Variables = [ new() { Codigo = "Inversion", Nombre = "Inversión", ValorPorDefecto = 250 }, new() { Codigo = "Flujo", Nombre = "Flujo", ValorPorDefecto = 100 },
                  new() { Codigo = "PlazoMeses", Nombre = "Plazo", ValorPorDefecto = 3 } ],
    Formulas = [ new() { Codigo = "FlujoPeriodo", Nombre = "Flujo", Expresion = "Flujo" }, new() { Codigo = "TasaPeriodo", Nombre = "Tasa", Expresion = "0,1" } ]
};
var rs = MotorEvaluacion.Evaluar(simple);
Caso("CF-17", "Cálculo financiero", "RF-02, RF-03", "VAN por flujo de caja contra el cálculo a mano", "Inversión 250; 3 flujos de 100; tasa 10 %", $"VAN = {N(vanManual, 4)} (a mano); NO VIABLE", rs.Exitoso ? $"VAN = {N(rs.Van, 4)}; {(rs.Viable ? "VIABLE" : "NO VIABLE")}" : string.Join(" ", rs.Errores), rs.Exitoso && Math.Abs(rs.Van - vanManual) < 1e-9 && !rs.Viable, "Calcula_el_VAN_igual_que_a_mano");

// rendimiento del cálculo (RNF-04)
var tiempos = new List<double>();
var plantillaA2 = PlantillaAnexo2.Crear();
for (int i = 0; i < 1000; i++) { var sw = Stopwatch.StartNew(); MotorEvaluacion.Evaluar(plantillaA2); tiempos.Add(sw.Elapsed.TotalMilliseconds); }
Caso("RN-00", "Rendimiento", "RNF-04", "Tiempo de una evaluación del Anexo 2 (9 escenarios)", "1.000 evaluaciones seguidas en el equipo de desarrollo", "Menos de 2 s por evaluación", $"Promedio {N(tiempos.Average(), 2)} ms; máximo {N(tiempos.Max(), 1)} ms", tiempos.Max() < 2000, "(medición del programa de evidencias)");

// ---------- 2. Reglas de negocio ----------
var hoy = DateTime.Today;
var prodAlDia = new Productor { Rut = "1-9", NivelCumplimiento = NivelesCumplimiento.AlDia };
var prodMoroso = new Productor { Rut = "1-9", NivelCumplimiento = NivelesCumplimiento.Moroso };
Faena F(string estado, DateTime? venc, int? oficina) => new() { Codigo = "1", Estado = estado, VencimientoPadron = venc, NroPadron = venc is null ? null : "P-1", IdOficina = oficina };
string Motivos(Productor p, Faena f) { var m = ReglasElegibilidad.Evaluar(p, f, hoy); return m.Count == 0 ? "Elegible" : string.Join("; ", m.Select(x => x.Mensaje)); }
Caso("RN-01", "Reglas de negocio", "RF-14", "Faena activa, padrón vigente, productor al día", "Faena activa con oficina; padrón vence en un año", "Elegible", Motivos(prodAlDia, F(EstadosFaena.Activa, hoy.AddYears(1), 1)), ReglasElegibilidad.Evaluar(prodAlDia, F(EstadosFaena.Activa, hoy.AddYears(1), 1), hoy).Count == 0, "La_faena_activa_con_padron_vigente_es_elegible");
Caso("RN-02", "Reglas de negocio", "RF-14", "Productor moroso", "Nivel de cumplimiento «Bajo» en la ficha", "No elegible, indicando el motivo", Motivos(prodMoroso, F(EstadosFaena.Activa, hoy.AddYears(1), 1)), ReglasElegibilidad.Evaluar(prodMoroso, F(EstadosFaena.Activa, hoy.AddYears(1), 1), hoy).Count > 0, "Bloquea_faena_inactiva_padron_vencido_y_productor_moroso_indicando_el_motivo");
Caso("RN-03", "Reglas de negocio", "RF-14", "Faena inactiva", "Estado de la faena «Inactiva»", "No elegible, indicando el motivo", Motivos(prodAlDia, F(EstadosFaena.Inactiva, hoy.AddYears(1), 1)), ReglasElegibilidad.Evaluar(prodAlDia, F(EstadosFaena.Inactiva, hoy.AddYears(1), 1), hoy).Count > 0, "Bloquea_faena_inactiva_padron_vencido_y_productor_moroso_indicando_el_motivo");
Caso("RN-04", "Reglas de negocio", "RF-14", "Padrón vencido", "El padrón venció ayer", "No elegible, indicando el motivo", Motivos(prodAlDia, F(EstadosFaena.Activa, hoy.AddDays(-1), 1)), ReglasElegibilidad.Evaluar(prodAlDia, F(EstadosFaena.Activa, hoy.AddDays(-1), 1), hoy).Count > 0, "Bloquea_faena_inactiva_padron_vencido_y_productor_moroso_indicando_el_motivo");
Caso("RN-05", "Reglas de negocio", "RF-14", "Faena sin oficina asignada", "Faena sin oficina en la ficha", "No elegible, indicando el motivo", Motivos(prodAlDia, F(EstadosFaena.Activa, hoy.AddYears(1), null)), ReglasElegibilidad.Evaluar(prodAlDia, F(EstadosFaena.Activa, hoy.AddYears(1), null), hoy).Count > 0, "Una_faena_sin_oficina_o_sin_padron_no_es_elegible");

var e = new Entorno();
var ing = e.Sesion(e.IdIngeniero);
string obligatorios = Error(() => e.Solicitudes.GuardarBorrador(null, new DatosSolicitud(), ing));
Caso("RN-06", "Reglas de negocio", "RF-15, RF-16", "Solicitud sin faena, instrumento ni monto", "Formulario vacío", "Se rechaza indicando los campos que faltan", obligatorios, obligatorios.Contains("faena") && obligatorios.Contains("monto"), "Exige_los_campos_obligatorios_de_la_solicitud");
var fuera = e.DatosCompletos(e.IdFaenaElegible);
var plantilla1 = e.Solicitudes.PlantillaPara(1)!;
var varRango = plantilla1.Variables.First(v => v.Maximo is not null && v.Origen == OrigenVariable.Formulario);
fuera.DatosProyecto[varRango.Codigo] = varRango.Maximo!.Value * 10;
var sim = e.Solicitudes.Simular(fuera);
Caso("RN-07", "Reglas de negocio", "RF-07", "Valor fuera del rango permitido", $"{varRango.Nombre} = {N(varRango.Maximo.Value * 10, 0)} (máximo {N(varRango.Maximo.Value, 0)})", "No calcula y muestra un mensaje claro", sim.Exitoso ? "Calculó" : string.Join(" ", sim.Errores), !sim.Exitoso, "Un_valor_fuera_de_rango_impide_calcular");
var mala = PlantillaEjemplo.Crear(); mala.Formulas[0].Expresion = "TonelajeMensual * (LeyCu";
var comp = MotorFormulas.Compilar(mala);
Caso("RN-08", "Reglas de negocio", "RF-06, RF-07", "Fórmula mal escrita en el editor", "«TonelajeMensual * (LeyCu» (falta un paréntesis)", "Se rechaza con un mensaje", comp.EsValido ? "Aceptada" : string.Join(" ", comp.Errores.Take(1)), !comp.EsValido, "Rechaza_formulas_mal_escritas");

// visibilidad por rol
var sol = e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible), ing);
int idVallenar = e.Db.Tabla<Oficina>().Single(o => o.Nombre == "Oficina Minera Vallenar").Id;
int jefeVallenar = e.Usuarios.Crear(new DatosUsuario { Correo = "jefe.vallenar@enami.cl", NombreCompleto = "Jefe Oficina Vallenar (prueba)", Rol = Roles.JefeOficina, IdOficina = idVallenar, Clave = "Clave2026" }, e.IdAdmin);
int visCopiapo = e.Solicitudes.Listar(e.Sesion(e.IdJefeCopiapo)).Count, visVallenar = e.Solicitudes.Listar(e.Sesion(jefeVallenar)).Count;
Caso("RN-09", "Reglas de negocio", "RF-10, RNF-03", "Cada jefe ve solo las solicitudes de su oficina", "Solicitud de una faena de Copiapó", "Jefe de Copiapó: 1; jefe de Vallenar: 0", $"Jefe de Copiapó: {visCopiapo}; jefe de Vallenar: {visVallenar}", visCopiapo == 1 && visVallenar == 0, "Cada_rol_ve_solo_las_solicitudes_que_le_corresponden");
int otroIng = e.Usuarios.Crear(new DatosUsuario { Correo = "otro.ingeniero@enami.cl", NombreCompleto = "Otro Ingeniero Copiapó (prueba)", Rol = Roles.Ingeniero, IdOficina = e.IdCopiapo, Clave = "Clave2026" }, e.IdAdmin);
string ajena = Error(() => e.Solicitudes.GuardarBorrador(sol.IdSolicitud, e.DatosCompletos(e.IdFaenaElegible), e.Sesion(otroIng)));
Caso("RN-10", "Reglas de negocio", "RF-10", "Solo quien registró la solicitud puede modificarla", "Otro ingeniero de la misma oficina intenta modificar la solicitud", "Se rechaza", ajena, ajena != "(no hubo error)", "Cada_rol_ve_solo_las_solicitudes_que_le_corresponden");
e.Usuarios.CambiarEstado(jefeVallenar, false, e.IdAdmin);
var login = e.Usuarios.Autenticar("jefe.vallenar@enami.cl", "Clave2026");
Caso("RN-11", "Reglas de negocio", "RF-11, RNF-03", "Un usuario deshabilitado no puede ingresar", "El administrador deshabilita la cuenta y el usuario intenta ingresar", "Ingreso rechazado", login is null ? "Ingreso rechazado" : "Ingresó", login is null, "Un_usuario_deshabilitado_no_puede_ingresar_y_su_sesion_deja_de_ser_valida");

// reglas de aprobación de fórmulas
var vig = e.Plantillas.ObtenerVigente(1)!;
var b = e.Plantillas.CrearBorrador(vig.Id, e.IdJefatura);
string porIngeniero = Error(() => e.Plantillas.Activar(b.Id, e.IdIngeniero));
Caso("RN-12", "Reglas de aprobación", "RF-06", "Un ingeniero intenta aprobar una versión de fórmulas", "Borrador v2 del crédito de emergencia", "Se rechaza: solo la Jefatura aprueba", porIngeniero, porIngeniero.Contains("Jefatura"), "Solo_la_Jefatura_aprueba_versiones_y_queda_en_la_auditoria");
b.Formulas.Single(f => f.Codigo == "FlujoPeriodo").Expresion = "IngresoMensual - VariableQueNoExiste";
e.Plantillas.GuardarBorrador(b);
string conErrores = Error(() => e.Plantillas.Activar(b.Id, e.IdJefatura));
Caso("RN-13", "Reglas de aprobación", "RF-06", "La Jefatura intenta aprobar un borrador con errores", "Fórmula que usa una variable inexistente", "Se rechaza: un borrador con errores no se activa", conErrores, conErrores.Contains("errores"), "No_permite_activar_un_borrador_con_errores_ni_modificar_una_version_vigente");
string vigenteMod = Error(() => e.Plantillas.GuardarBorrador(vig));
Caso("RN-14", "Reglas de aprobación", "RF-06, RF-08", "Modificar una versión vigente", "Guardar cambios sobre la v1 vigente", "Se rechaza: un cambio siempre crea una versión nueva", vigenteMod, vigenteMod != "(no hubo error)", "No_permite_activar_un_borrador_con_errores_ni_modificar_una_version_vigente");
b.Formulas.Single(f => f.Codigo == "FlujoPeriodo").Expresion = "IngresoMensual - CostoFlete - CostoOperacion";
e.Plantillas.GuardarBorrador(b);
e.Plantillas.Activar(b.Id, e.IdJefatura);
var vB = e.Db.Tabla<VersionPlantilla>().Single(x => x.Id == b.Id);
Caso("RN-15", "Reglas de aprobación", "RF-06", "La Jefatura aprueba un borrador válido", "Borrador v2 corregido", "v2 vigente, v1 retirada; queda el aprobador y la fecha", $"v{vB.Version} {vB.Estado}; v1 {e.Db.Tabla<VersionPlantilla>().Single(x => x.Id == vig.Id).Estado}; aprobador: Jefatura de Fomento (demo); fecha registrada", vB.Estado == "VIGENTE" && vB.IdAprobador == e.IdJefatura, "El_repositorio_versiona_y_activa_plantillas");
string borrarUsada = e.Plantillas.MotivoNoEliminable(vig.Id) ?? "(se puede eliminar)";
Caso("RN-16", "Reglas de aprobación", "RF-08", "Eliminar una versión usada en una evaluación", "La v1 retirada se usó en la solicitud guardada", "No se puede eliminar", borrarUsada, borrarUsada.Contains("evaluación"), "Se_eliminan_borradores_y_versiones_retiradas_sin_uso");
var res = e.Db.Tabla<ResultadoCalculo>().Single(x => x.IdSolicitud == sol.IdSolicitud);
int nValores = e.Db.Tabla<ValorEntrada>().Count(v => v.IdResultado == res.Id);
Caso("TR-01", "Trazabilidad", "RF-08", "La evaluación guardada conserva la versión y los valores usados", "Solicitud calculada con la v1 y luego aprobada la v2", "El resultado sigue ligado a la v1 y guarda los valores de entrada", $"Resultado ligado a la versión v{e.Db.Tabla<VersionPlantilla>().Single(x => x.Id == res.IdPlantilla).Version}; {nValores} valores de entrada guardados", res.IdPlantilla == vig.Id && nValores > 0, "La_evaluacion_guardada_queda_ligada_a_su_version_a_sus_valores_y_a_su_autor");

// ingreso fallido y registro de recursos para la traza
e.Usuarios.Autenticar("ingeniero.copiapo@enami.cl", "clave-equivocada");
e.Usuarios.Autenticar("ingeniero.copiapo@enami.cl", Entorno.Clave);
e.Fichas.GuardarRecurso(e.IdFaenaElegible, new DatosRecurso { Categoria = CategoriasRecurso.Indicado, Toneladas = 30_000, LeyCuPct = 1.2m, LeyAuGt = 6 }, ing);
var nombres = e.Db.Tabla<Usuario>().ToDictionary(u => u.Id, u => u.NombreCompleto);
var traza = e.Db.Tabla<Auditoria>().ToList().OrderBy(a => a.Id).Where(a => a.Accion != "SISTEMA").Select(a => new Dictionary<string, string>
{
    ["n"] = a.Id.ToString(), ["fecha"] = a.Fecha.ToString("dd-MM-yyyy HH:mm:ss"), ["usuario"] = a.IdUsuario is int u ? nombres[u] : (a.Robot ?? "Sistema"),
    ["accion"] = a.Accion, ["solicitud"] = a.IdSolicitud?.ToString() ?? "", ["detalle"] = a.Detalle ?? ""
}).ToList();
var acciones = traza.Select(x => x["accion"]).ToHashSet();
Caso("TR-02", "Trazabilidad", "RF-27, RNF-08", "Cada acción queda en la auditoría con fecha y autor", "Secuencia: alta de usuario, solicitud, cálculo, versión creada y aprobada, recurso, ingreso fallido", "USUARIO_CREADO, SOLICITUD_CREADA, CALCULO, VERSION_CREADA, VERSION_APROBADA, RECURSO_FAENA, INGRESO_FALLIDO", string.Join(", ", new[] { "USUARIO_CREADO", "SOLICITUD_CREADA", "CALCULO", "VERSION_CREADA", "VERSION_APROBADA", "RECURSO_FAENA", "INGRESO_FALLIDO" }.Where(acciones.Contains)), new[] { "USUARIO_CREADO", "SOLICITUD_CREADA", "CALCULO", "VERSION_CREADA", "VERSION_APROBADA", "RECURSO_FAENA", "INGRESO_FALLIDO" }.All(acciones.Contains), "Solo_la_Jefatura_aprueba_versiones_y_queda_en_la_auditoria");

// ---------- 3. Importación de las planillas reales (anonimizadas) ----------
var cargas = new List<Dictionary<string, string>>();
if (args.Length > 0 && Directory.Exists(args[0]))
{
    var db = new AlmacenMemoria();
    new Semilla(db, new HasherPrueba()).Ejecutar(new OpcionesSemilla { ClaveAdministrador = "Clave2026" });
    var carga = new ServicioCargaPlanillas(db, new ServicioAuditoria(db));
    foreach (var c in new Semilla(db, new HasherPrueba()).CargarPlanillasIniciales(new OpcionesSemilla { RutaPlanillas = args[0] }, carga))
        cargas.Add(new() { ["planilla"] = c.Planilla, ["leidas"] = c.FilasLeidas.ToString(), ["nuevos"] = c.Nuevos.ToString(), ["actualizados"] = c.Actualizados.ToString(),
                           ["rechazadas"] = c.Rechazadas.Count.ToString(), ["observaciones"] = c.Observaciones.Count.ToString(), ["segundos"] = c.Duracion.TotalSeconds.ToString("0.00", es) });
}

File.WriteAllText(args.Length > 1 ? args[1] : "evidencias.json", JsonSerializer.Serialize(new { fecha = DateTime.Now.ToString("dd-MM-yyyy HH:mm"), casos, traza, cargas },
    new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
foreach (var c in casos) Console.WriteLine($"{c["id"]} {c["resultado"]}: {c["obtenido"]}");
Console.WriteLine($"{casos.Count(c => c["resultado"] == "Aprobado")} de {casos.Count} casos aprobados; {traza.Count} registros de auditoría; {cargas.Count} planillas");
