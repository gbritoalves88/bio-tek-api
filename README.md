# Bio-Tek API

API backend da plataforma **Bio-Tek**, uma aplicação para leitores organizarem suas bibliotecas, acompanharem hábitos de leitura e interagirem com outros usuários.

A plataforma permite gerenciar livros e coleções, registrar avaliações e comentários, acompanhar leituras, participar da comunidade e descobrir livros por meio de busca textual ou reconhecimento visual de capas.

## Tecnologias

- **.NET 8** — plataforma de desenvolvimento
- **ASP.NET Core Web API** — construção dos endpoints HTTP
- **C#** — linguagem principal
- **MongoDB** — banco de dados NoSQL
- **MongoDB.Driver** — acesso ao banco de dados
- **JWT Bearer** — autenticação e autorização
- **Google OAuth 2.0** — autenticação com conta Google
- **Swagger / OpenAPI** — documentação dos endpoints
- **SkiaSharp** — processamento de imagens
- **Perceptual Hash (pHash)** — comparação de similaridade visual entre capas
- **Firebase Cloud Messaging (FCM)** — envio de notificações push
- **Cloudflare R2** — armazenamento de imagens compatível com API S3
- **Docker** — containerização e publicação da aplicação

## Funcionalidades

### Usuários e autenticação
- Login com Google OAuth 2.0
- Geração e validação de tokens JWT
- Renovação de tokens (*refresh token*)
- Logout
- Cadastro e gerenciamento de perfis
- Nickname e perfil público
- Preferências de tema
- Aceite de termos de uso
- Controle de usuários e permissões administrativas

### Biblioteca e catálogo
- Cadastro, edição e remoção de livros
- Consulta de livros e detalhes
- Busca por ISBN
- Busca e consulta de catálogo
- Importação de livros para a biblioteca pessoal
- Registro e atualização do progresso de leitura
- Consulta de atividades de leitura

### Coleções e avaliações
- Criação e gerenciamento de coleções
- Associação de livros às coleções
- Avaliação de livros e coleções
- Comentários em livros e coleções
- Consulta de avaliações e comentários

### Comunidade
- Feed de atividades da comunidade
- Curtidas em publicações
- Sistema de seguidores
- Consulta de seguidores e usuários seguidos
- Perfis públicos e atividades dos usuários

### Hábitos de leitura
- Registro de sequências de leitura (*streaks*)
- Histórico de leitura
- Métricas mensais
- Lembretes automáticos de leitura
- Notificações relacionadas à continuidade do hábito

### Busca visual por capas
A API possui um mecanismo de busca visual que permite localizar livros a partir de uma imagem de capa.

O processo utiliza **Perceptual Hash (pHash)**:
1. A imagem recebida é processada para gerar um hash perceptual.
2. O hash é comparado com os hashes previamente armazenados.
3. A similaridade visual é calculada entre as imagens.
4. Os resultados são ordenados pela similaridade e retornados ao usuário.

Os hashes são armazenados no MongoDB, com suporte a cache em memória para otimizar as consultas.

### Upload e armazenamento de imagens
- Upload de imagens por meio da API
- Armazenamento no Cloudflare R2
- Geração de URLs públicas para acesso às imagens
- Exclusão de arquivos armazenados

### Notificações
- Integração com Firebase Cloud Messaging
- Registro de dispositivos para recebimento de notificações
- Inscrição e remoção de inscrições de notificações
- Consulta e gerenciamento de notificações
- Envio de notificações para usuários
- Lembretes automatizados de leitura

### Administração
- Dashboard administrativo
- Gerenciamento de usuários
- Ativação e desativação de contas
- Moderação de comentários
- Gerenciamento de livros
- Exportação de dados
- Rotinas administrativas de manutenção

## Arquitetura

A solução está organizada em quatro projetos, separando responsabilidades da aplicação:

```text
BioTek.sln
└── src/
    ├── BioTek.Api/
    │   ├── Controllers/
    │   ├── Models/
    │   ├── Program.cs
    │   └── BioTek.Api.csproj
    │
    ├── BioTek.Application/
    │   ├── Helpers/
    │   ├── Services/
    │   └── BioTek.Application.csproj
    │
    ├── BioTek.Domain/
    │   ├── Common/
    │   ├── DTOs/
    │   ├── Entities/
    │   ├── Enums/
    │   ├── Interfaces/
    │   └── BioTek.Domain.csproj
    │
    └── BioTek.Infrastructure/
        ├── Data/
        ├── Repositories/
        ├── Services/
        └── BioTek.Infrastructure.csproj
```

### Responsabilidades

| Projeto | Responsabilidade |
|---|---|
| `BioTek.Api` | Endpoints HTTP, autenticação, autorização, middlewares e configuração da aplicação |
| `BioTek.Application` | Serviços, regras de aplicação, processamento de imagens e tarefas em segundo plano |
| `BioTek.Domain` | Entidades, interfaces, DTOs, enums e contratos do domínio |
| `BioTek.Infrastructure` | Persistência MongoDB, repositórios e serviços de infraestrutura |

O acesso aos dados é centralizado por meio de interfaces de repositório, com implementação baseada no MongoDB.

## Banco de dados

O projeto utiliza MongoDB como banco de dados principal.

Entre as entidades utilizadas estão:

- Usuários
- Livros
- Catálogo
- Coleções
- Avaliações de livros
- Avaliações de coleções
- Comentários
- Seguidores
- Curtidas
- Notificações
- Inscrições de notificações
- Registros de leitura
- Sequências de leitura
- Hashes perceptuais de capas

O banco utilizado pela aplicação é identificado como `BioTek`.

## Segurança e proteção da API

A API utiliza autenticação JWT Bearer, com validação de emissor, público-alvo, validade do token e assinatura.

Também possui:
- Autorização por endpoint, com uso de `[Authorize]` e `[AllowAnonymous]`.
- Políticas de rate limiting para autenticação, uploads, administração, notificações e requisições gerais.
- Configuração de CORS.
- Endpoint de health check para monitoramento.
- Configurações e credenciais externas fornecidas por configuração da aplicação e variáveis de ambiente.

## Configuração

As configurações sensíveis não devem ser versionadas no repositório. Utilize variáveis de ambiente ou mecanismos locais de gerenciamento de segredos.

Principais configurações utilizadas:

| Configuração | Finalidade |
|---|---|
| `ConnectionStrings:MongoDb` | Conexão com o MongoDB |
| `Jwt:Key` | Chave de assinatura dos tokens JWT |
| `Jwt:Issuer` | Emissor dos tokens |
| `Jwt:Audience` | Público-alvo dos tokens |
| `FIREBASE_PROJECT_ID` | Identificador do projeto Firebase |
| `FIREBASE_SERVICE_ACCOUNT_JSON` | Credenciais da conta de serviço Firebase em JSON |
| `R2:AccountId` | Identificador da conta Cloudflare |
| `R2:AccessKey` | Chave de acesso ao armazenamento R2 |
| `R2:SecretKey` | Chave secreta do armazenamento R2 |
| `R2:BucketName` | Nome do bucket |
| `R2:PublicUrl` | URL pública utilizada para acessar os arquivos |

A configuração de ofertas de livros também é carregada pela seção `BookOffers`.

Para execução local, configure os valores necessários em `appsettings.Development.json`, User Secrets ou variáveis de ambiente, sem publicar credenciais.

## Executando localmente

### Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Instância MongoDB local ou MongoDB Atlas
- Credenciais das integrações utilizadas pela aplicação, quando necessárias

### 1. Clone o repositório

```bash
git clone https://github.com/gbritoalves88/bio-tek-api.git
```

### 2. Acesse a pasta do projeto

```bash
cd bio-tek-api
```

### 3. Configure as variáveis de ambiente

Configure a conexão com MongoDB, as opções de JWT e as integrações que deseja utilizar.

Não compartilhe nem versione chaves privadas, tokens ou arquivos de credenciais.

### 4. Restaure as dependências

```bash
dotnet restore BioTek.sln
```

### 5. Execute a API

```bash
dotnet run --project src/BioTek.Api/BioTek.Api.csproj
```

A aplicação será iniciada conforme as configurações do ambiente de execução.

## Executando com Docker

O repositório possui um `Dockerfile` com build em múltiplos estágios:

1. Utiliza a imagem do .NET SDK 8 para restaurar dependências e compilar a aplicação.
2. Publica a aplicação em modo Release.
3. Utiliza uma imagem menor do ASP.NET 8 para executar a API.

Para construir a imagem:

```bash
docker build -t biotek-api .
```

Para executar o container:

```bash
docker run -d \
  --name biotek-api \
  -p 8080:8080 \
  --env ASPNETCORE_ENVIRONMENT=Production \
  biotek-api
```

As configurações necessárias devem ser fornecidas ao container por variáveis de ambiente ou pelo ambiente de hospedagem.

A aplicação escuta na porta `8080` dentro do container.

## Endpoints e documentação

Os endpoints são organizados por controllers, utilizando o prefixo:

```text
/api/{controller}
```

Alguns dos principais grupos são:

| Controller | Responsabilidade |
|---|---|
| `Auth` | Autenticação e gerenciamento de tokens |
| `Usuario` | Usuários e perfis |
| `Livro` | Biblioteca e livros |
| `Catalogo` | Consulta e importação de catálogo |
| `Colecao` | Coleções |
| `Avaliacao` | Avaliações de livros |
| `AvaliacaoColecao` | Avaliações de coleções |
| `ComentarioLivro` | Comentários em livros |
| `ComentarioColecao` | Comentários em coleções |
| `Comunidade` | Feed e curtidas |
| `Seguidor` | Relacionamentos entre usuários |
| `Streak` | Sequências e métricas de leitura |
| `Notificacao` | Notificações e dispositivos |
| `Upload` | Upload de imagens |
| `CapaHash` | Busca visual por capas |
| `Offers` | Ofertas de livros |
| `Admin` | Funcionalidades administrativas |

A documentação interativa dos endpoints é disponibilizada pelo Swagger.

O endpoint de health check é:

```http
GET /health
```

## Tarefas em segundo plano

A aplicação possui serviços hospedados (*Background Services*) para executar rotinas automáticas, incluindo:

- Lembretes de leitura.
- Notificações relacionadas às sequências de leitura.
- Rotinas de manutenção periódica.

Esses serviços são registrados junto ao ciclo de vida da aplicação.

## Deploy

A aplicação possui suporte à publicação por meio de Docker, utilizando o `Dockerfile` localizado na raiz do repositório.

O ambiente de produção utiliza configurações externas para conexão com o banco de dados e integrações, evitando manter credenciais diretamente no código-fonte.

---

## Projeto relacionado

**Bio-Tek** é uma plataforma de leitura que conecta organização de bibliotecas pessoais, acompanhamento de hábitos e interação entre leitores.

Repositório da API:  
https://github.com/gbritoalves88/bio-tek-api
