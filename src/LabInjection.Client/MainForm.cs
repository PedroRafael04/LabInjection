namespace LabInjection.Client;

/// <summary>
/// Tela principal do cliente. Contem uma aba por operacao (Buscar/SELECT,
/// Inserir/INSERT, Atualizar/UPDATE, Excluir/DELETE e um modo "SQL Livre"),
/// e uma area de resultado compartilhada por todas elas: o SQL que o
/// servidor efetivamente gerou, uma mensagem de status e uma grade com as
/// linhas devolvidas (quando houver).
/// </summary>
public class MainForm : Form
{
    private readonly ApiClient _api;

    private readonly Label _lblConectado = new();
    private readonly CheckBox _chkModoSeguro = new();
    private readonly TabControl _tabs = new();
    private readonly TextBox _txtSqlGerado = new();
    private readonly Label _lblStatus = new();
    private readonly DataGridView _grid = new();

    // Buscar
    private readonly TextBox _txtBuscarNome = new();

    // Inserir
    private readonly TextBox _txtInserirNome = new();
    private readonly TextBox _txtInserirDescricao = new();
    private readonly TextBox _txtInserirPreco = new();
    private readonly TextBox _txtInserirEstoque = new();

    // Atualizar
    private readonly TextBox _txtAtualizarId = new();
    private readonly TextBox _txtAtualizarNome = new();
    private readonly TextBox _txtAtualizarDescricao = new();
    private readonly TextBox _txtAtualizarPreco = new();
    private readonly TextBox _txtAtualizarEstoque = new();

    // Excluir
    private readonly TextBox _txtExcluirId = new();

    // SQL livre
    private readonly TextBox _txtSqlLivre = new();

    public MainForm(ApiClient api, string host, int porta)
    {
        _api = api;

        Text = "LabInjection - Cliente";
        ClientSize = new Size(900, 680);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 520);

        _lblConectado.Text = $"Conectado como '{_api.Usuario}' em {host}:{porta}   |   Interface SEM protecao contra SQL Injection (uso academico).";
        _lblConectado.Dock = DockStyle.Top;
        _lblConectado.Height = 26;
        _lblConectado.TextAlign = ContentAlignment.MiddleLeft;
        _lblConectado.Padding = new Padding(8, 0, 0, 0);
        _lblConectado.BackColor = Color.LightGoldenrodYellow;

        // Opcao central do laboratorio: liga/desliga o tratamento de SQL
        // Injection no servidor para as abas Buscar/Inserir/Atualizar/
        // Excluir. Com a caixa desmarcada (padrao), o servidor monta o SQL
        // concatenando o texto digitado (vulneravel); marcada, o servidor
        // usa consultas parametrizadas (NpgsqlParameter) para os mesmos
        // campos - permitindo repetir exatamente o mesmo "ataque" nos dois
        // modos e comparar o resultado. Nao afeta a aba "SQL Livre": ali o
        // usuario digita o comando inteiro, nao ha um filtro para proteger
        // com parametros (ver comentario no Program.cs do servidor).
        _chkModoSeguro.Text = "Modo seguro no servidor (consultas parametrizadas) - nao se aplica a aba \"SQL Livre\"";
        _chkModoSeguro.Dock = DockStyle.Top;
        _chkModoSeguro.Height = 24;
        _chkModoSeguro.Padding = new Padding(8, 2, 0, 0);
        _chkModoSeguro.Checked = false;

        _tabs.Dock = DockStyle.Top;
        _tabs.Height = 230;
        _tabs.TabPages.Add(CriarAbaBuscar());
        _tabs.TabPages.Add(CriarAbaInserir());
        _tabs.TabPages.Add(CriarAbaAtualizar());
        _tabs.TabPages.Add(CriarAbaExcluir());
        _tabs.TabPages.Add(CriarAbaSqlLivre());

        var lblSqlTitulo = new Label
        {
            Text = "SQL gerado e enviado ao PostgreSQL pelo servidor:",
            Dock = DockStyle.Top,
            Height = 20,
            Padding = new Padding(8, 4, 0, 0),
        };

        _txtSqlGerado.Multiline = true;
        _txtSqlGerado.ReadOnly = true;
        _txtSqlGerado.Dock = DockStyle.Top;
        _txtSqlGerado.Height = 55;
        _txtSqlGerado.Font = new Font("Consolas", 9F);
        _txtSqlGerado.BackColor = Color.White;

        _lblStatus.Dock = DockStyle.Top;
        _lblStatus.Height = 24;
        _lblStatus.Padding = new Padding(8, 2, 0, 0);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

        // Ordem de adicao importa: no WinForms, controles com Dock=Top vao
        // ocupando o topo na ordem em que sao adicionados a colecao Controls
        // (o primeiro adicionado fica mais acima), e o controle com
        // Dock=Fill (a grade) ocupa todo o espaco que restar por ultimo.
        Controls.Add(_grid);
        Controls.Add(_lblStatus);
        Controls.Add(_txtSqlGerado);
        Controls.Add(lblSqlTitulo);
        Controls.Add(_tabs);
        Controls.Add(_chkModoSeguro);
        Controls.Add(_lblConectado);
    }

    private TabPage CriarAbaBuscar()
    {
        var pagina = new TabPage("Buscar (SELECT)");

        AddLabel(pagina, "Nome do produto (ou parte dele):", 16, 20);
        _txtBuscarNome.Bounds = new Rectangle(16, 42, 400, 24);

        var btn = new Button { Text = "Buscar", Bounds = new Rectangle(430, 41, 100, 26) };
        // ExecutarComBloqueio desabilita o botao durante a chamada, para
        // impedir que o usuario dispare varias buscas em paralelo clicando
        // repetidas vezes (o que poderia fazer respostas fora de ordem
        // sobrescreverem a grade de forma inconsistente).
        btn.Click += async (_, _) => await ExecutarComBloqueio(btn,
            () => _api.BuscarAsync(_txtBuscarNome.Text, _chkModoSeguro.Checked));

        var dica = new Label
        {
            // Payload testado manualmente contra o schema de exemplo: a
            // tela de busca so deveria devolver produtos, mas o UNION faz
            // o servidor devolver tambem CPF e cartao de credito da tabela
            // "clientes" (que essa tela nunca deveria tocar). So funciona
            // com o "Modo seguro" desmarcado.
            Text = "Dica de injecao (modo cru): x' UNION SELECT id, nome, cpf || ' / ' || cartao_credito, 0, 0 FROM clientes --",
            Bounds = new Rectangle(16, 80, 760, 18),
            ForeColor = Color.DimGray,
            Font = new Font(Font, FontStyle.Italic),
        };

        pagina.Controls.AddRange(new Control[] { _txtBuscarNome, btn, dica });
        return pagina;
    }

    private TabPage CriarAbaInserir()
    {
        var pagina = new TabPage("Inserir (INSERT)");

        AddLabel(pagina, "Nome:", 16, 20);
        _txtInserirNome.Bounds = new Rectangle(110, 18, 250, 24);

        AddLabel(pagina, "Descricao:", 16, 55);
        _txtInserirDescricao.Bounds = new Rectangle(110, 53, 250, 24);

        AddLabel(pagina, "Preco:", 16, 90);
        _txtInserirPreco.Bounds = new Rectangle(110, 88, 120, 24);

        AddLabel(pagina, "Estoque:", 250, 90);
        _txtInserirEstoque.Bounds = new Rectangle(340, 88, 100, 24);

        var btn = new Button { Text = "Inserir", Bounds = new Rectangle(16, 125, 100, 28) };
        btn.Click += async (_, _) => await ExecutarComBloqueio(btn, () => _api.InserirAsync(
            _txtInserirNome.Text, _txtInserirDescricao.Text, _txtInserirPreco.Text, _txtInserirEstoque.Text,
            _chkModoSeguro.Checked));

        pagina.Controls.AddRange(new Control[]
        {
            _txtInserirNome, _txtInserirDescricao, _txtInserirPreco, _txtInserirEstoque, btn,
        });
        return pagina;
    }

    private TabPage CriarAbaAtualizar()
    {
        var pagina = new TabPage("Atualizar (UPDATE)");

        AddLabel(pagina, "Id:", 16, 20);
        _txtAtualizarId.Bounds = new Rectangle(110, 18, 80, 24);

        AddLabel(pagina, "Nome:", 210, 20);
        _txtAtualizarNome.Bounds = new Rectangle(300, 18, 200, 24);

        AddLabel(pagina, "Descricao:", 16, 55);
        _txtAtualizarDescricao.Bounds = new Rectangle(110, 53, 250, 24);

        AddLabel(pagina, "Preco:", 16, 90);
        _txtAtualizarPreco.Bounds = new Rectangle(110, 88, 120, 24);

        AddLabel(pagina, "Estoque:", 250, 90);
        _txtAtualizarEstoque.Bounds = new Rectangle(340, 88, 100, 24);

        var btn = new Button { Text = "Atualizar", Bounds = new Rectangle(16, 125, 100, 28) };
        btn.Click += async (_, _) => await ExecutarComBloqueio(btn, () => _api.AtualizarAsync(
            _txtAtualizarId.Text, _txtAtualizarNome.Text, _txtAtualizarDescricao.Text,
            _txtAtualizarPreco.Text, _txtAtualizarEstoque.Text, _chkModoSeguro.Checked));

        pagina.Controls.AddRange(new Control[]
        {
            _txtAtualizarId, _txtAtualizarNome, _txtAtualizarDescricao,
            _txtAtualizarPreco, _txtAtualizarEstoque, btn,
        });
        return pagina;
    }

    private TabPage CriarAbaExcluir()
    {
        var pagina = new TabPage("Excluir (DELETE)");

        AddLabel(pagina, "Id:", 16, 20);
        _txtExcluirId.Bounds = new Rectangle(110, 18, 150, 24);

        var btn = new Button { Text = "Excluir", Bounds = new Rectangle(16, 55, 100, 28) };
        btn.Click += async (_, _) => await ExecutarComBloqueio(btn,
            () => _api.ExcluirAsync(_txtExcluirId.Text, _chkModoSeguro.Checked));

        var dica = new Label
        {
            // No modo seguro este payload e rejeitado pela validacao do
            // servidor (nao e um numero inteiro) antes de qualquer SQL ser
            // executado - um bom contraste para mostrar em aula.
            Text = "Dica de injecao (modo cru): 1 OR 1=1   (apaga todas as linhas da tabela produtos)",
            Bounds = new Rectangle(16, 95, 760, 18),
            ForeColor = Color.DimGray,
            Font = new Font(Font, FontStyle.Italic),
        };

        pagina.Controls.AddRange(new Control[] { _txtExcluirId, btn, dica });
        return pagina;
    }

    private TabPage CriarAbaSqlLivre()
    {
        var pagina = new TabPage("SQL Livre");

        AddLabel(pagina, "Comando SQL (SELECT ou DML):", 16, 10);
        _txtSqlLivre.Multiline = true;
        _txtSqlLivre.Font = new Font("Consolas", 9F);
        _txtSqlLivre.Bounds = new Rectangle(16, 32, 760, 100);

        var btn = new Button { Text = "Executar", Bounds = new Rectangle(16, 140, 100, 28) };
        btn.Click += async (_, _) => await ExecutarComBloqueio(btn, () => _api.ExecutarSqlAsync(_txtSqlLivre.Text));

        var aviso = new Label
        {
            // Diferenca importante em relacao as outras abas: aqui nao e o
            // caso classico de "SQL Injection via campo de formulario" - o
            // usuario digita o comando inteiro, nao um valor que deveria
            // caber dentro de um filtro pronto. Por isso o checkbox "Modo
            // seguro" no topo da janela nao tem efeito nesta aba: nao ha
            // consulta fixa do servidor para proteger com parametros.
            Text = "Esta aba sempre executa o comando cru, sem parametros - o \"Modo seguro\" nao se aplica aqui.",
            Bounds = new Rectangle(16, 175, 760, 18),
            ForeColor = Color.DimGray,
            Font = new Font(Font, FontStyle.Italic),
        };

        pagina.Controls.AddRange(new Control[] { _txtSqlLivre, btn, aviso });
        return pagina;
    }

    private static void AddLabel(Control pai, string texto, int x, int y)
    {
        pai.Controls.Add(new Label { Text = texto, Location = new Point(x, y), AutoSize = true });
    }

    // Desabilita o botao que disparou a acao enquanto a chamada esta em
    // andamento (e garante que ele volte a ficar habilitado no final, mesmo
    // se der erro), evitando disparos duplicados por clique repetido.
    private async Task ExecutarComBloqueio(Button botao, Func<Task<QueryResult?>> chamada)
    {
        botao.Enabled = false;
        try
        {
            await ExecutarAsync(chamada);
        }
        finally
        {
            botao.Enabled = true;
        }
    }

    // Chama a API e, independente do resultado, atualiza a area compartilhada
    // de resultado (SQL gerado, status e grade). Erros de rede/comunicacao
    // (servidor fora do ar, timeout etc.) sao tratados aqui; erros do
    // PostgreSQL em si (sintaxe invalida, permissao negada) ja chegam dentro
    // do QueryResult.Erro e sao tratados em MostrarResultado.
    private async Task ExecutarAsync(Func<Task<QueryResult?>> chamada)
    {
        _lblStatus.ForeColor = Color.Black;
        _lblStatus.Text = "Executando...";

        try
        {
            var resultado = await chamada();
            MostrarResultado(resultado);
        }
        catch (Exception ex)
        {
            _txtSqlGerado.Text = "";
            _lblStatus.ForeColor = Color.Firebrick;
            _lblStatus.Text = $"Erro de comunicacao com o servidor: {ex.Message}";
            _grid.DataSource = null;
        }
    }

    // Preenche a area de resultado a partir da resposta do servidor:
    //   - sem resultado -> mensagem de erro generica;
    //   - com campo Erro preenchido -> mostra o erro do Postgres;
    //   - com LinhasAfetadas preenchido -> foi um DML, mostra quantas linhas
    //     foram alteradas e nao ha grade para montar;
    //   - caso contrario -> foi um SELECT, monta as colunas e linhas na
    //     DataGridView dinamicamente (o numero de colunas varia de acordo
    //     com a consulta, inclusive quando a consulta foi alterada por uma
    //     injecao SQL, como no UNION SELECT da dica da aba Buscar).
    private void MostrarResultado(QueryResult? resultado)
    {
        _grid.DataSource = null;
        _grid.Columns.Clear();

        if (resultado is null)
        {
            _lblStatus.ForeColor = Color.Firebrick;
            _lblStatus.Text = "Resposta vazia do servidor.";
            return;
        }

        _txtSqlGerado.Text = resultado.SqlGerado;

        if (resultado.Erro is not null)
        {
            _lblStatus.ForeColor = Color.Firebrick;
            _lblStatus.Text = $"Erro do PostgreSQL: {resultado.Erro}";
            return;
        }

        if (resultado.LinhasAfetadas is int afetadas)
        {
            _lblStatus.ForeColor = Color.DarkGreen;
            _lblStatus.Text = $"OK - {afetadas} linha(s) afetada(s).";
            return;
        }

        _lblStatus.ForeColor = Color.DarkGreen;
        _lblStatus.Text = $"OK - {resultado.Linhas.Count} linha(s) retornada(s).";

        foreach (var coluna in resultado.Colunas)
            _grid.Columns.Add(coluna, coluna);

        foreach (var linha in resultado.Linhas)
            _grid.Rows.Add(linha.Select(v => (object)(v ?? "NULL")).ToArray());
    }
}
