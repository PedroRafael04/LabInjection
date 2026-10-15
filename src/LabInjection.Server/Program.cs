using System.Globalization;
using Npgsql;

// ============================================================================
// LabInjection.Server
//
// API HTTP minima que faz de "ponte" entre a interface cliente (WinForms) e
// o PostgreSQL. A ideia e simples: o cliente manda usuario/senha + os dados
// digitados pelo usuario, o servidor monta um comando SQL e o executa no
// banco, devolvendo o resultado como JSON.
//
// Cada uma das quatro rotas de produto (buscar/inserir/atualizar/excluir)
// aceita um campo "Seguro" (bool), enviado pelo cliente, que decide COMO o
// SQL vai ser montado:
//
//   - Seguro = false (padrao, modo "cru"): o texto digitado pelo usuario e
//     concatenado/interpolado direto dentro do comando SQL. E exatamente
//     isso que abre a porta para SQL Injection.
//   - Seguro = true (modo "tratado"): o comando SQL usa marcadores de
//     parametro (@nome, @id, ...) e os valores digitados sao enviados
//     separadamente, via NpgsqlParameter. O driver nunca interpreta um
//     parametro como parte do COMANDO, so como DADO - entao nao importa o
//     que o usuario digite, ele nao consegue alterar a estrutura da query.
//
// Essa opcao existe para o aluno poder comparar, na pratica, o mesmo ataque
// nos dois modos: no modo cru ele funciona; no modo tratado, o "ataque"
// digitado e tratado como um valor de busca/insercao literal (ou falha de
// validacao, quando o campo deveria ser numerico).
//
// A rota de "SQL livre" (/api/sql/executar) NAO tem modo seguro: ali o
// usuario digita o comando completo, nao um valor dentro de um filtro. Nao
// existe uma "consulta legitima" para proteger com parametros - parametrizar
// so faz sentido quando ha uma query fixa no servidor com um "buraco" onde
// o valor do usuario entra.
// ============================================================================

var builder = WebApplication.CreateBuilder(args);

// Escuta em todas as interfaces IPv4 da maquina (0.0.0.0) para que outros
// clientes na rede local possam se conectar usando o IP desta maquina.
builder.WebHost.UseUrls("http://0.0.0.0:5229");

var app = builder.Build();

const string PgHost = "localhost";
const int PgPort = 5432;
const string PgDatabase = "labinjection";

// Monta a connection string do Npgsql usando o NpgsqlConnectionStringBuilder
// (em vez de concatenar texto manualmente). Isso evita um segundo tipo de
// injecao - a "connection string injection" - em que um usuario/senha com
// caracteres especiais (por exemplo ';') poderia sobrescrever outras chaves
// da connection string (como Database=). O builder trata esse escaping.
//
// Importante: isso NAO tem relacao com o modo seguro/cru explicado acima -
// essa parte protege apenas a conexao, nao as consultas SQL.
static string BuildConnectionString(string usuario, string senha)
{
    var csb = new NpgsqlConnectionStringBuilder
    {
        Host = PgHost,
        Port = PgPort,
        Database = PgDatabase,
        Username = usuario,
        Password = senha,
    };
    return csb.ConnectionString;
}

// Login = apenas tentar abrir uma conexao Postgres com o usuario/senha
// informados. Se o Postgres aceitar (usuario existe, senha correta, tem
// permissao de CONNECT no banco "labinjection"), consideramos autenticado.
// Nao existe tabela de usuarios da aplicacao: a autenticacao "e" a do
// proprio banco de dados.
app.MapPost("/api/login", async (LoginRequest req) =>
{
    try
    {
        await using var conn = new NpgsqlConnection(BuildConnectionString(req.Usuario, req.Senha));
        await conn.OpenAsync();
        return Results.Ok(new { ok = true, mensagem = $"Login bem-sucedido como '{req.Usuario}'." });
    }
    catch (Exception ex)
    {
        // Qualquer falha ao abrir a conexao (usuario inexistente, senha
        // errada, sem permissao de conectar etc.) e tratada como login
        // invalido e devolvida com status 401.
        return Results.Json(new { ok = false, mensagem = ex.Message }, statusCode: 401);
    }
});

app.MapPost("/api/produtos/buscar", async (BuscarRequest req) =>
{
    if (req.Seguro)
    {
        // Modo tratado: "%...%" e o valor completo do parametro, nunca uma
        // fatia do texto do comando. Uma aspa simples digitada pelo usuario
        // vira parte do DADO buscado, nao do SQL.
        string sqlSeguro = "SELECT id, nome, descricao, preco, estoque FROM produtos " +
                            "WHERE nome LIKE @nome ORDER BY id";
        return await ExecutarSeguroAsync(req.Usuario, req.Senha, sqlSeguro,
            new NpgsqlParameter("nome", $"%{req.Nome}%"));
    }

    // Modo cru (vulneravel): o valor digitado pelo usuario (req.Nome) entra
    // direto dentro das aspas simples da clausula LIKE. Qualquer aspa
    // simples ou palavra-chave SQL digitada passa a fazer parte do
    // comando em vez de ser tratada como texto puro.
    string sql = $"SELECT id, nome, descricao, preco, estoque FROM produtos " +
                 $"WHERE nome LIKE '%{req.Nome}%' ORDER BY id";
    return await ExecutarCruAsync(req.Usuario, req.Senha, sql);
});

app.MapPost("/api/produtos/inserir", async (InserirRequest req) =>
{
    if (req.Seguro)
    {
        // No modo tratado, preco/estoque tambem sao validados e convertidos
        // para o tipo correto ANTES de ir para o banco - no modo cru eles
        // sao jogados direto no SQL como texto, permitindo que um ataque
        // use o campo numerico para injetar SQL arbitrario.
        if (!TentarConverterNumeros(req.Preco, req.Estoque, out var preco, out var estoque, out var erroConversao))
            return RespostaErroValidacao(erroConversao);

        string sqlSeguro = "INSERT INTO produtos (nome, descricao, preco, estoque) " +
                            "VALUES (@nome, @descricao, @preco, @estoque)";
        return await ExecutarSeguroAsync(req.Usuario, req.Senha, sqlSeguro,
            new NpgsqlParameter("nome", req.Nome),
            new NpgsqlParameter("descricao", req.Descricao),
            new NpgsqlParameter("preco", preco),
            new NpgsqlParameter("estoque", estoque));
    }

    string sql = $"INSERT INTO produtos (nome, descricao, preco, estoque) " +
                 $"VALUES ('{req.Nome}', '{req.Descricao}', {req.Preco}, {req.Estoque})";
    return await ExecutarCruAsync(req.Usuario, req.Senha, sql);
});

app.MapPost("/api/produtos/atualizar", async (AtualizarRequest req) =>
{
    if (req.Seguro)
    {
        if (!int.TryParse(req.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return RespostaErroValidacao($"Id invalido: '{req.Id}' nao e um numero inteiro.");
        if (!TentarConverterNumeros(req.Preco, req.Estoque, out var preco, out var estoque, out var erroConversao))
            return RespostaErroValidacao(erroConversao);

        string sqlSeguro = "UPDATE produtos SET nome = @nome, descricao = @descricao, " +
                            "preco = @preco, estoque = @estoque WHERE id = @id";
        return await ExecutarSeguroAsync(req.Usuario, req.Senha, sqlSeguro,
            new NpgsqlParameter("nome", req.Nome),
            new NpgsqlParameter("descricao", req.Descricao),
            new NpgsqlParameter("preco", preco),
            new NpgsqlParameter("estoque", estoque),
            new NpgsqlParameter("id", id));
    }

    string sql = $"UPDATE produtos SET nome = '{req.Nome}', descricao = '{req.Descricao}', " +
                 $"preco = {req.Preco}, estoque = {req.Estoque} WHERE id = {req.Id}";
    return await ExecutarCruAsync(req.Usuario, req.Senha, sql);
});

app.MapPost("/api/produtos/excluir", async (ExcluirRequest req) =>
{
    if (req.Seguro)
    {
        // Este e o caso mais didatico: "1 OR 1=1" nunca sera interpretado
        // pelo Postgres como inteiro, entao a validacao abaixo rejeita o
        // ataque antes mesmo de qualquer SQL ser enviado ao banco.
        if (!int.TryParse(req.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return RespostaErroValidacao($"Id invalido: '{req.Id}' nao e um numero inteiro.");

        string sqlSeguro = "DELETE FROM produtos WHERE id = @id";
        return await ExecutarSeguroAsync(req.Usuario, req.Senha, sqlSeguro, new NpgsqlParameter("id", id));
    }

    string sql = $"DELETE FROM produtos WHERE id = {req.Id}";
    return await ExecutarCruAsync(req.Usuario, req.Senha, sql);
});

// Modo "SQL livre": a interface cliente envia o comando SELECT/DML inteiro,
// digitado pelo usuario, e o servidor apenas repassa para o Postgres sem
// nenhuma analise do texto. Nao tem modo seguro (ver explicacao no topo
// do arquivo).
app.MapPost("/api/sql/executar", async (SqlLivreRequest req) =>
{
    return await ExecutarCruAsync(req.Usuario, req.Senha, req.Comando);
});

app.Run();

// ----------------------------------------------------------------------------
// Modo CRU (vulneravel): recebe o comando SQL ja pronto, como texto, e
// executa exatamente como veio - sem nenhum parametro. Qualquer palavra-
// chave ou aspa que o usuario tenha digitado em algum campo do formulario
// (e que acabou dentro desse texto, na rota que montou "sql") passa a fazer
// parte do comando real executado no Postgres.
// ----------------------------------------------------------------------------
async Task<IResult> ExecutarCruAsync(string usuario, string senha, string sql)
{
    try
    {
        await using var conn = new NpgsqlConnection(BuildConnectionString(usuario, senha));
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(sql, conn);
        return await LerResultadoAsync(cmd, sql);
    }
    catch (Exception ex)
    {
        // Devolve 200 OK mesmo em erro de SQL de proposito: o campo "Erro"
        // e o "SqlGerado" permitem que o cliente mostre exatamente o que foi
        // enviado ao banco e por que falhou, sem precisar tratar codigos de
        // status HTTP diferentes so para exibir uma mensagem de erro.
        return Results.Ok(new QueryResult(sql, Array.Empty<string>(), new List<string?[]>(), null, ex.Message));
    }
}

// ----------------------------------------------------------------------------
// Modo SEGURO (tratado): recebe um comando SQL com marcadores de parametro
// (ex.: "WHERE nome LIKE @nome") e a lista de parametros com os valores
// reais. O Npgsql manda o texto do comando e os valores em mensagens
// separadas para o Postgres - o banco nunca "reinterpreta" o valor como
// parte do comando, entao nao ha como um valor digitado pelo usuario alterar
// a estrutura da consulta.
//
// Para o aluno poder comparar com o modo cru na mesma tela, o SQL "para
// exibicao" devolvido ao cliente mostra o texto parametrizado seguido dos
// valores que foram enviados (apenas para fins de visualizacao - os valores
// continuam sendo entregues ao Postgres separadamente, nao concatenados).
// ----------------------------------------------------------------------------
async Task<IResult> ExecutarSeguroAsync(string usuario, string senha, string sqlParametrizado, params NpgsqlParameter[] parametros)
{
    string sqlExibicao = parametros.Length == 0
        ? sqlParametrizado
        : $"{sqlParametrizado}   -- parametros enviados separadamente (nao concatenados no SQL): " +
          string.Join(", ", parametros.Select(p => $"{p.ParameterName} = {FormatarParametroExibicao(p.Value)}"));

    try
    {
        await using var conn = new NpgsqlConnection(BuildConnectionString(usuario, senha));
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(sqlParametrizado, conn);
        cmd.Parameters.AddRange(parametros);

        return await LerResultadoAsync(cmd, sqlExibicao);
    }
    catch (Exception ex)
    {
        return Results.Ok(new QueryResult(sqlExibicao, Array.Empty<string>(), new List<string?[]>(), null, ex.Message));
    }
}

// ----------------------------------------------------------------------------
// Executa o comando (ja montado, cru ou parametrizado - nao faz diferenca
// aqui) e devolve o resultado no mesmo formato para o cliente:
//
//   1. Executa com ExecuteReaderAsync. O Npgsql permite usar o mesmo metodo
//      tanto para SELECT quanto para INSERT/UPDATE/DELETE.
//   2. Se o comando NAO devolve colunas (reader.FieldCount == 0), entende-se
//      que foi um comando DML (INSERT/UPDATE/DELETE) - nesse caso o dado
//      relevante e apenas o numero de linhas afetadas.
//   3. Se o comando devolve colunas (SELECT), le-se o nome de cada coluna e,
//      linha a linha, o valor de cada campo, convertendo tudo para string
//      (usando CultureInfo.InvariantCulture para numeros/datas, evitando que
//      o resultado mude de formato dependendo do idioma configurado no
//      servidor) para poder ser serializado em JSON de forma simples.
// ----------------------------------------------------------------------------
async Task<IResult> LerResultadoAsync(NpgsqlCommand cmd, string sqlExibicao)
{
    await using var reader = await cmd.ExecuteReaderAsync();

    if (reader.FieldCount == 0)
    {
        int linhasAfetadas = reader.RecordsAffected;
        return Results.Ok(new QueryResult(sqlExibicao, Array.Empty<string>(), new List<string?[]>(), linhasAfetadas, null));
    }

    var colunas = new string[reader.FieldCount];
    for (int i = 0; i < reader.FieldCount; i++)
        colunas[i] = reader.GetName(i);

    var linhas = new List<string?[]>();
    while (await reader.ReadAsync())
    {
        var linha = new string?[reader.FieldCount];
        for (int i = 0; i < reader.FieldCount; i++)
            linha[i] = FormatarValor(reader, i);
        linhas.Add(linha);
    }

    return Results.Ok(new QueryResult(sqlExibicao, colunas, linhas, null, null));
}

// Converte o valor de uma coluna para string de forma independente de
// idioma/localidade (InvariantCulture), para que numeros decimais sempre
// usem "." e nao "," (o que aconteceria se o servidor rodasse com a
// localidade pt-BR, por exemplo) e para manter datas em um formato fixo.
static string? FormatarValor(NpgsqlDataReader reader, int indice)
{
    if (reader.IsDBNull(indice))
        return null;

    object valor = reader.GetValue(indice);
    return valor switch
    {
        IFormattable formatavel => formatavel.ToString(null, CultureInfo.InvariantCulture),
        _ => valor.ToString(),
    };
}

// Usado so para montar o texto de exibicao do modo seguro (nunca entra em
// um comando SQL - os valores reais vao para o Postgres via NpgsqlParameter).
static string FormatarParametroExibicao(object? valor) => valor switch
{
    null => "NULL",
    string texto => $"'{texto}'",
    IFormattable formatavel => formatavel.ToString(null, CultureInfo.InvariantCulture),
    _ => valor.ToString() ?? "NULL",
};

// Tenta converter preco/estoque (recebidos como string, do mesmo jeito que
// no modo cru) para os tipos numericos reais usados pela tabela "produtos".
// E essa validacao - que so existe no modo seguro - que rejeita de cara um
// valor como "1; DROP TABLE produtos" digitado no campo de preco: o
// TryParse falha e o pedido nem chega a ser executado no banco.
static bool TentarConverterNumeros(string precoTexto, string estoqueTexto, out decimal preco, out int estoque, out string erro)
{
    if (!decimal.TryParse(precoTexto, NumberStyles.Number, CultureInfo.InvariantCulture, out preco))
    {
        estoque = 0;
        erro = $"Preco invalido: '{precoTexto}' nao e um numero (use ponto como separador decimal, ex.: 99.90).";
        return false;
    }

    if (!int.TryParse(estoqueTexto, NumberStyles.Integer, CultureInfo.InvariantCulture, out estoque))
    {
        erro = $"Estoque invalido: '{estoqueTexto}' nao e um numero inteiro.";
        return false;
    }

    erro = "";
    return true;
}

// Resposta padrao quando a validacao do modo seguro rejeita um valor antes
// de qualquer SQL ser montado - por isso "SqlGerado" fica vazio.
static IResult RespostaErroValidacao(string mensagem) =>
    Results.Ok(new QueryResult("(nenhum SQL foi executado - validacao do modo seguro rejeitou o valor)",
        Array.Empty<string>(), new List<string?[]>(), null, mensagem));

record LoginRequest(string Usuario, string Senha);
record BuscarRequest(string Usuario, string Senha, string Nome, bool Seguro);
record InserirRequest(string Usuario, string Senha, string Nome, string Descricao, string Preco, string Estoque, bool Seguro);
record AtualizarRequest(string Usuario, string Senha, string Id, string Nome, string Descricao, string Preco, string Estoque, bool Seguro);
record ExcluirRequest(string Usuario, string Senha, string Id, bool Seguro);
record SqlLivreRequest(string Usuario, string Senha, string Comando);
record QueryResult(string SqlGerado, string[] Colunas, List<string?[]> Linhas, int? LinhasAfetadas, string? Erro);
