# Bio-Tek API

## 📚 Visão Geral

O **Bio-Tek** é uma plataforma social para leitores de:

- 📖 Livros
- 📘 Mangás
- 📙 HQs

O sistema foi idealizado para permitir que usuários organizem suas leituras, avaliem obras, compartilhem opiniões e interajam socialmente com outros leitores.

---

# 🚀 Funcionalidades

## ✅ Implementadas

- Cadastro manual de livros
- Avaliação de livros
- Comentários em avaliações de livros
- Criação de coleções
- Avaliação de coleções
- Comentários em avaliações de coleções
- Sistema social de seguidores
- Perfil público com nickname (`@usuario`)
- Tema claro/escuro
- Login com Google OAuth
- JWT Authentication
- Endpoints protegidos

---

## ⬜ Em desenvolvimento

- Feed da comunidade
- Upload de imagens
- Login Facebook
- Deploy cloud
- Docker Compose

---

# 🧠 Arquitetura Backend

O backend foi desenvolvido utilizando:

- .NET 8
- ASP.NET Core Web API
- MongoDB
- Clean Architecture
- JWT Bearer Authentication
- Google OAuth 2.0
- Swagger/OpenAPI

---

# 📁 Estrutura do Projeto

```text
src/
 ├── BioTek.Api
 │    ├── Controllers
 │    ├── Middlewares
 │    ├── Program.cs
 │    └── Swagger
 │
 ├── BioTek.Application
 │    ├── Services
 │    ├── DTOs
 │    └── Rules
 │
 ├── BioTek.Domain
 │    ├── Common
 │    ├── Entities
 │    ├── Enums
 │    └── Interfaces
 │
 └── BioTek.Infrastructure
      ├── MongoDB
      ├── Repositories
      ├── Authentication
      └── Configurations

# 🧩 Estrutura do Banco de Dados

---

# 👤 Usuario

Representa o usuário da plataforma.

| Campo                 | Tipo      | Descrição                   |
| --------------------- | --------- | --------------------------- |
| Id                    | string    | Identificador único         |
| IdGoogle              | string?   | ID do login Google          |
| IdFacebook            | string?   | ID do login Facebook        |
| Nome                  | string    | Nome do usuário             |
| Email                 | string    | Email                       |
| Descricao             | string?   | Biografia/perfil            |
| Tema                  | string    | Claro/Escuro                |
| QuantidadeLivros      | int       | Total de livros cadastrados |
| QuantidadeLivrosLidos | int       | Total de livros lidos       |
| MediaAvaliacoes       | double    | Média das avaliações        |
| Adm                   | bool      | Define se é administrador   |
| Ativo                 | bool      | Usuário ativo/inativo       |
| DataCriacao           | DateTime  | Data de criação             |
| DataAtualizacao       | DateTime? | Última atualização          |

---

# 📚 Livro

Representa um livro, mangá ou HQ cadastrado pelo usuário.

| Campo           | Tipo      | Descrição             |
| --------------- | --------- | --------------------- |
| Id              | string    | Identificador único   |
| IdUsuario       | string    | Usuário dono do livro |
| IdColecao       | string?   | Coleção vinculada     |
| Titulo          | string    | Nome do livro         |
| Autor           | string    | Autor                 |
| Descricao       | string?   | Descrição do livro    |
| CodigoBarras    | string?   | Código de barras      |
| Imagem          | string?   | URL/imagem da capa    |
| Lido            | bool      | Define se foi lido    |
| DataCriacao     | DateTime  | Data de criação       |
| DataAtualizacao | DateTime? | Última atualização    |

---

# 🗂️ Colecao

Representa coleções de livros, mangás ou HQs.

Exemplo:

* Berserk
* Harry Potter
* Sandman
* One Piece

| Campo           | Tipo      | Descrição            |
| --------------- | --------- | -------------------- |
| Id              | string    | Identificador único  |
| Nome            | string    | Nome da coleção      |
| Titulo          | string    | Título principal     |
| Descricao       | string?   | Descrição da coleção |
| Imagem          | string?   | Imagem da coleção    |
| DataCriacao     | DateTime  | Data de criação      |
| DataAtualizacao | DateTime? | Última atualização   |

---

# ⭐ AvaliacaoLivro

Representa a avaliação de um livro.

## ⚠️ Regra de Negócio

Um livro só poderá ser avaliado caso:

* esteja marcado como lido

| Campo       | Tipo     | Descrição           |
| ----------- | -------- | ------------------- |
| Id          | string   | Identificador único |
| IdLivro     | string   | Livro avaliado      |
| IdUsuario   | string   | Usuário avaliador   |
| Nota        | int      | Nota de 0 a 5       |
| DataCriacao | DateTime | Data da avaliação   |

---

# 💬 ComentarioLivro

Representa comentários em avaliações de livros.

## ⚠️ Regra de Negócio

Comentários só poderão existir em livros avaliados.

| Campo       | Tipo     | Descrição            |
| ----------- | -------- | -------------------- |
| Id          | string   | Identificador único  |
| IdLivro     | string   | Livro relacionado    |
| IdUsuario   | string   | Usuário que comentou |
| Comentario  | string   | Texto do comentário  |
| DataCriacao | DateTime | Data do comentário   |

---

# ⭐ AvaliacaoColecao

Representa a avaliação de uma coleção.

## ⚠️ Regra de Negócio

Coleções só poderão ser avaliadas caso:

* o usuário possua livros vinculados à coleção
* ao menos um livro esteja marcado como lido

| Campo       | Tipo     | Descrição           |
| ----------- | -------- | ------------------- |
| Id          | string   | Identificador único |
| IdColecao   | string   | Coleção avaliada    |
| IdUsuario   | string   | Usuário avaliador   |
| Nota        | int      | Nota de 0 a 5       |
| DataCriacao | DateTime | Data da avaliação   |

---

# 💬 ComentarioColecao

Representa comentários em avaliações de coleções.

## ⚠️ Regra de Negócio

Comentários só poderão existir em coleções avaliadas.

| Campo       | Tipo     | Descrição            |
| ----------- | -------- | -------------------- |
| Id          | string   | Identificador único  |
| IdColecao   | string   | Coleção relacionada  |
| IdUsuario   | string   | Usuário que comentou |
| Comentario  | string   | Texto do comentário  |
| DataCriacao | DateTime | Data do comentário   |

---

# 👥 Seguidor

Representa o relacionamento social entre usuários.

| Campo       | Tipo     | Descrição              |
| ----------- | -------- | ---------------------- |
| Id          | string   | Identificador único    |
| IdUsuario   | string   | Usuário seguido        |
| IdSeguidor  | string   | Usuário que segue      |
| DataCriacao | DateTime | Data do relacionamento |

---

# 🔐 Autenticação

Inicialmente será utilizado:

* Login Google
* Login Facebook

Objetivos:

* evitar cadastro manual
* simplificar autenticação
* aumentar segurança

O backend armazenará apenas:

* IDs externos
* nome
* email
* informações básicas do perfil

---

# 🌙 Tema do Sistema

O usuário poderá escolher:

* Tema Claro
* Tema Escuro

A preferência será armazenada no perfil.

---

# 🚀 Roadmap Técnico

## Backend

* Estrutura Clean Architecture
* MongoDB
* Docker Compose
* Swagger
* JWT/Auth Social

## Futuro

* Upload de imagens
* API de livros
* Feed inteligente
* Sistema de curtidas
* Recomendações
* App mobile

---

# 🧠 Objetivo da Arquitetura

O projeto está sendo estruturado para:

* aprendizado profissional
* escalabilidade
* performance
* fácil manutenção
* futura migração mobile
* preparação cloud/docker
