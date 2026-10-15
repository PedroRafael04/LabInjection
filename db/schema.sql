-- ============================================================================
-- LabInjection - script de criação do banco de dados
-- Projeto academico para demonstrar os riscos de SQL Injection.
-- Execute com: psql -U postgres -h localhost -f db/schema.sql
-- ============================================================================

DROP DATABASE IF EXISTS labinjection;
CREATE DATABASE labinjection;

\c labinjection

CREATE TABLE produtos (
    id        SERIAL PRIMARY KEY,
    nome      TEXT NOT NULL,
    descricao TEXT,
    preco     NUMERIC(10,2) NOT NULL,
    estoque   INTEGER NOT NULL DEFAULT 0
);

-- Tabela "sensível" que a aplicação NUNCA consulta diretamente.
-- Serve para demonstrar que, via UNION-based SQL Injection, um atacante
-- pode ler dados fora do que a tela de produtos deveria expor.
CREATE TABLE clientes (
    id             SERIAL PRIMARY KEY,
    nome           TEXT NOT NULL,
    cpf            TEXT NOT NULL,
    cartao_credito TEXT NOT NULL
);

INSERT INTO produtos (nome, descricao, preco, estoque) VALUES
 ('Teclado Mecanico',  'Teclado gamer RGB switch azul', 350.00, 15),
 ('Mouse Optico',      'Mouse sem fio 1600dpi',         120.00, 30),
 ('Monitor 27 polegadas','Monitor Full HD IPS',         899.90,  8),
 ('Headset Gamer',     'Headset com microfone',         250.00, 20),
 ('Cadeira Gamer',     'Cadeira ergonomica reclinavel', 1200.00,  5);

INSERT INTO clientes (nome, cpf, cartao_credito) VALUES
 ('Ana Souza',  '123.456.789-00', '4111 1111 1111 1111'),
 ('Bruno Lima', '987.654.321-00', '5500 0000 0000 0004'),
 ('Carla Dias', '111.222.333-44', '3400 0000 0000 009');

-- ----------------------------------------------------------------------------
-- Role usado para logar na interface cliente (login = usuario/senha do Postgres)
-- ----------------------------------------------------------------------------
DROP ROLE IF EXISTS lab_user;
CREATE ROLE lab_user LOGIN PASSWORD 'lab123';

GRANT CONNECT ON DATABASE labinjection TO lab_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON produtos TO lab_user;
GRANT USAGE, SELECT ON SEQUENCE produtos_id_seq TO lab_user;

-- Propositalmente concedido: em muitos ambientes reais a conta de aplicação
-- tem privilegios mais amplos do que a propria tela usa. Isso é o que permite
-- que a injeção na busca de produtos também exponha a tabela "clientes".
GRANT SELECT ON clientes TO lab_user;
