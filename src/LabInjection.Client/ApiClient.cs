using System.Net.Http.Json;
using System.Text.Json;

namespace LabInjection.Client;

/// <summary>
/// Cliente HTTP simples que fala com o LabInjection.Server.
/// Reenvia usuario/senha em cada chamada - o servidor abre uma conexao
/// Postgres nova por requisicao usando essas credenciais.
/// </summary>
public sealed class ApiClient
{
    // PropertyNameCaseInsensitive porque o servidor devolve JSON com nomes
    // em camelCase (ex.: "sqlGerado") e os records do cliente usam PascalCase
    // (ex.: "SqlGerado") - sem essa opcao o System.Text.Json nao casaria os
    // nomes e os campos chegariam vazios/nulos.
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;

    public string Usuario { get; }
    public string Senha { get; }

    public ApiClient(string host, int porta, string usuario, string senha)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri($"http://{host}:{porta}/"),
            Timeout = TimeSpan.FromSeconds(15),
        };
        Usuario = usuario;
        Senha = senha;
    }

    // Tenta autenticar enviando usuario/senha ao endpoint de login. O
    // servidor nao devolve um token de sessao: a cada chamada seguinte
    // (Buscar, Inserir, etc.) o mesmo usuario/senha e reenviado, e o
    // servidor abre uma nova conexao Postgres a cada requisicao.
    public async Task<LoginResponse?> LoginAsync()
    {
        var resp = await _http.PostAsJsonAsync("api/login", new { Usuario, Senha });
        return await resp.Content.ReadFromJsonAsync<LoginResponse>(JsonOpts);
    }

    // Cada metodo abaixo so monta o payload da respectiva rota e delega
    // para o PostAsync generico - toda a logica de SQL fica no servidor, o
    // cliente apenas envia o texto digitado pelo usuario sem validar.
    //
    // O parametro "seguro" viaja junto no payload e decide, no servidor, se
    // o SQL vai ser montado por concatenacao direta (false, modo cru/
    // vulneravel) ou com consultas parametrizadas (true, modo tratado) -
    // ver comentario no topo de LabInjection.Server/Program.cs.
    public Task<QueryResult?> BuscarAsync(string nome, bool seguro) =>
        PostAsync("api/produtos/buscar", new { Usuario, Senha, Nome = nome, Seguro = seguro });

    public Task<QueryResult?> InserirAsync(string nome, string descricao, string preco, string estoque, bool seguro) =>
        PostAsync("api/produtos/inserir", new { Usuario, Senha, Nome = nome, Descricao = descricao, Preco = preco, Estoque = estoque, Seguro = seguro });

    public Task<QueryResult?> AtualizarAsync(string id, string nome, string descricao, string preco, string estoque, bool seguro) =>
        PostAsync("api/produtos/atualizar", new { Usuario, Senha, Id = id, Nome = nome, Descricao = descricao, Preco = preco, Estoque = estoque, Seguro = seguro });

    public Task<QueryResult?> ExcluirAsync(string id, bool seguro) =>
        PostAsync("api/produtos/excluir", new { Usuario, Senha, Id = id, Seguro = seguro });

    // Sem parametro "seguro": a rota de SQL livre nao tem modo tratado,
    // porque o usuario digita o comando inteiro, nao um valor dentro de um
    // filtro pronto (ver comentario no servidor).
    public Task<QueryResult?> ExecutarSqlAsync(string comando) =>
        PostAsync("api/sql/executar", new { Usuario, Senha, Comando = comando });

    private async Task<QueryResult?> PostAsync(string rota, object payload)
    {
        var resp = await _http.PostAsJsonAsync(rota, payload);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<QueryResult>(JsonOpts);
    }
}

public record LoginResponse(bool Ok, string Mensagem);

public record QueryResult(string SqlGerado, string[] Colunas, List<string?[]> Linhas, int? LinhasAfetadas, string? Erro);
