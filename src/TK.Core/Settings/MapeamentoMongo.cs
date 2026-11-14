using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;

namespace TK.Core.Settings;

// Definição de como o MongoDB deve serializar/deserializar os tipos de dados do projeto.
// Deve ser chamado uma única vez, ao iniciar a aplicação
public static class MapeamentoMongo
{
    private static bool _registrado;

    public static void Registrar()
    {
        // Evita a execução múltipla do mapeamento. Após o sucesso na primeira vez, não precisa mais ser chamado.
        if (_registrado)
        {
            return;
        }

        // Padronização do Guid
        BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

        // Enums gravados como texto ("Pendente") em vez de número (0)
        var convencoes = new ConventionPack
        {
            new EnumRepresentationConvention(BsonType.String)
        };
        ConventionRegistry.Register("EnumComoTexto", convencoes, _ => true);

        _registrado = true;
    }
}