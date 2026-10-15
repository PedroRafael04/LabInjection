namespace LabInjection.Client;

/// <summary>
/// Tela inicial do cliente: pede o endereco do servidor (IP + porta) e as
/// credenciais do PostgreSQL. Ao autenticar com sucesso, abre a MainForm e
/// se esconde (nao se fecha ainda, pois e ela quem mantem o loop de
/// mensagens da aplicacao vivo - ver comentario no construtor).
/// </summary>
public class LoginForm : Form
{
    private readonly TextBox _txtHost = new();
    private readonly TextBox _txtPorta = new();
    private readonly TextBox _txtUsuario = new();
    private readonly TextBox _txtSenha = new();
    private readonly Button _btnEntrar = new();
    private readonly Label _lblStatus = new();

    public LoginForm()
    {
        Text = "LabInjection - Login";
        ClientSize = new Size(380, 270);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        // Layout simples: cada campo e posicionado manualmente (Bounds) em
        // vez de usar um Designer.cs - como a tela e pequena e fixa, isso
        // evita gerar um arquivo de designer so para 4 campos e 1 botao.
        AddLabel("Servidor (IP):", 20, 22);
        _txtHost.Bounds = new Rectangle(170, 19, 180, 24);
        _txtHost.Text = "127.0.0.1";

        AddLabel("Porta:", 20, 57);
        _txtPorta.Bounds = new Rectangle(170, 54, 180, 24);
        _txtPorta.Text = "5229";

        AddLabel("Usuario (Postgres):", 20, 92);
        _txtUsuario.Bounds = new Rectangle(170, 89, 180, 24);
        _txtUsuario.Text = "lab_user";

        AddLabel("Senha:", 20, 127);
        _txtSenha.Bounds = new Rectangle(170, 124, 180, 24);
        _txtSenha.UseSystemPasswordChar = true;
        _txtSenha.Text = "lab123";

        _btnEntrar.Text = "Entrar";
        _btnEntrar.Bounds = new Rectangle(170, 165, 100, 30);
        _btnEntrar.Click += BtnEntrar_Click;

        _lblStatus.Bounds = new Rectangle(20, 210, 340, 50);
        _lblStatus.ForeColor = Color.Firebrick;

        var lblAviso = new Label
        {
            Text = "Uso academico: interface sem protecao contra SQL Injection.",
            Bounds = new Rectangle(20, 2, 340, 16),
            Font = new Font(Font, FontStyle.Italic),
            ForeColor = Color.DimGray,
        };

        Controls.AddRange(new Control[]
        {
            lblAviso, _txtHost, _txtPorta, _txtUsuario, _txtSenha, _btnEntrar, _lblStatus,
        });

        // Permite logar pressionando Enter em qualquer campo, sem precisar
        // clicar no botao "Entrar".
        AcceptButton = _btnEntrar;
    }

    private void AddLabel(string texto, int x, int y)
    {
        Controls.Add(new Label { Text = texto, Location = new Point(x, y), AutoSize = true });
    }

    // Fluxo do login:
    //   1. Desabilita o botao para evitar duplo clique durante a chamada.
    //   2. Valida a porta digitada (precisa ser um numero).
    //   3. Cria um ApiClient com host/porta/usuario/senha e chama LoginAsync,
    //      que faz uma requisicao HTTP ao servidor.
    //   4. Se o servidor confirmar (Ok == true), abre a MainForm passando o
    //      ApiClient ja autenticado e esconde esta tela.
    //   5. Se falhar (credenciais invalidas ou erro de rede), mostra a
    //      mensagem de erro e deixa o usuario tentar novamente.
    private async void BtnEntrar_Click(object? sender, EventArgs e)
    {
        _btnEntrar.Enabled = false;
        _lblStatus.ForeColor = Color.Black;
        _lblStatus.Text = "Conectando...";

        try
        {
            if (!int.TryParse(_txtPorta.Text.Trim(), out int porta))
            {
                _lblStatus.ForeColor = Color.Firebrick;
                _lblStatus.Text = "Porta invalida.";
                return;
            }

            var api = new ApiClient(_txtHost.Text.Trim(), porta, _txtUsuario.Text.Trim(), _txtSenha.Text);
            var resposta = await api.LoginAsync();

            if (resposta is { Ok: true })
            {
                var main = new MainForm(api, _txtHost.Text.Trim(), porta);

                // Esta LoginForm e a janela principal da aplicacao (passada
                // para Application.Run em Program.cs). Por isso ela nao e
                // fechada aqui, apenas escondida - se fosse fechada, o loop
                // de mensagens terminaria e a aplicacao inteira encerraria
                // junto. Quando a MainForm for fechada pelo usuario, o
                // evento abaixo fecha a LoginForm tambem, encerrando o app.
                main.FormClosed += (_, _) => Close();
                Hide();
                main.Show();
            }
            else
            {
                _lblStatus.ForeColor = Color.Firebrick;
                _lblStatus.Text = resposta?.Mensagem ?? "Falha no login.";
            }
        }
        catch (Exception ex)
        {
            // Cobre falhas de rede/conexao (servidor fora do ar, IP errado,
            // firewall bloqueando a porta etc.), que nao chegam a produzir
            // uma resposta HTTP para o bloco acima tratar.
            _lblStatus.ForeColor = Color.Firebrick;
            _lblStatus.Text = $"Erro ao conectar: {ex.Message}";
        }
        finally
        {
            _btnEntrar.Enabled = true;
        }
    }
}
